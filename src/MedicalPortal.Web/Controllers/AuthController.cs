using System.Security.Cryptography;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Observability;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("AuthPolicy")]
public class AuthController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ITotpService _totp;
    private readonly ISessionService _sessions;
    private readonly IAuditService _audit;
    private readonly IAccessControlService _accessControl;

    public AuthController(
        MedicalPortalDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        ITotpService totp,
        ISessionService sessions,
        IAuditService audit,
        IAccessControlService accessControl)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _totp = totp;
        _sessions = sessions;
        _audit = audit;
        _accessControl = accessControl;
    }

    public record LoginRequest(string Email, string Password);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Enter a valid email and password." });

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var account = await _db.UserAccounts
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (account == null)
        {
            MedicalPortalDiagnostics.LoginsTotal.Add(1, new KeyValuePair<string, object?>("status", "failed"), new KeyValuePair<string, object?>("reason", "unknown_account"));
            await _hasher.VerifyDummyPasswordAsync(request.Password);
            await _audit.LogAsync("LOGIN_FAILED", "UserAccount", metadata: new { email = normalizedEmail, reason = "unknown_account" }, ipAddress: ip, userAgent: userAgent);
            return Unauthorized(new { error = "Invalid email or password." });
        }

        if (!account.IsActive)
        {
            await _audit.LogAsync("LOGIN_FAILED", "UserAccount", account.Id.ToString(), account.Id, metadata: new { reason = "inactive" }, ipAddress: ip, userAgent: userAgent);
            return Unauthorized(new { error = "This account has been deactivated." });
        }

        if (account.LockedUntil.HasValue && account.LockedUntil > DateTime.UtcNow)
        {
            var remainingMins = (int)Math.Ceiling((account.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return Unauthorized(new { error = $"Too many failed attempts. Try again in {remainingMins} minute(s)." });
        }

        var ok = _hasher.VerifyPassword(account.PasswordHash, request.Password);
        if (!ok)
        {
            account.FailedLoginCount++;
            var shouldLock = account.FailedLoginCount >= 5;
            if (shouldLock)
            {
                account.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            }
            await _db.SaveChangesAsync();

            await _audit.LogAsync("LOGIN_FAILED", "UserAccount", account.Id.ToString(), account.Id, metadata: new { attempt = account.FailedLoginCount, locked = shouldLock }, ipAddress: ip, userAgent: userAgent);
            return Unauthorized(new { error = "Invalid email or password." });
        }

        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        account.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var isClinicianOrAdmin = account.Role != UserRole.PATIENT;

        // MFA Evaluation: Mandatory for CLINICIAN, ADMIN, and staff roles, optional for PATIENT
        if (isClinicianOrAdmin)
        {
            if (!account.MfaEnabled)
            {
                var setupToken = _tokens.GenerateMfaChallengeToken(account.Id);
                await _audit.LogAsync("LOGIN_MFA_SETUP_REQUIRED", "UserAccount", account.Id.ToString(), account.Id, ipAddress: ip, userAgent: userAgent);

                return Ok(new
                {
                    mfaSetupRequired = true,
                    mfaToken = setupToken,
                    message = "Two-factor authentication enrollment is mandatory for your role."
                });
            }

            var challengeToken = _tokens.GenerateMfaChallengeToken(account.Id);
            await _audit.LogAsync("LOGIN_MFA_CHALLENGE", "UserAccount", account.Id.ToString(), account.Id, ipAddress: ip, userAgent: userAgent);

            return Ok(new
            {
                mfaRequired = true,
                mfaToken = challengeToken,
                message = "Two-factor authentication code required."
            });
        }
        else if (account.MfaEnabled)
        {
            // Patient opted-in to MFA
            var challengeToken = _tokens.GenerateMfaChallengeToken(account.Id);
            await _audit.LogAsync("LOGIN_MFA_CHALLENGE", "UserAccount", account.Id.ToString(), account.Id, ipAddress: ip, userAgent: userAgent);

            return Ok(new
            {
                mfaRequired = true,
                mfaToken = challengeToken,
                message = "Two-factor authentication code required."
            });
        }

        // Direct login for patient without MFA
        var sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var token = _tokens.GenerateAccessToken(account, account.PersonId, sid);
        var (session, refreshToken) = await _sessions.CreateSessionAsync(account.Id, sid, ip, userAgent);

        SetSessionCookie(token);

        MedicalPortalDiagnostics.LoginsTotal.Add(1, new KeyValuePair<string, object?>("status", "success"), new KeyValuePair<string, object?>("role", account.Role.ToString()));
        await _audit.LogAsync("LOGIN_SUCCEEDED", "UserAccount", account.Id.ToString(), account.Id, metadata: new { sid }, ipAddress: ip, userAgent: userAgent);

        return Ok(new
        {
            accessToken = token,
            refreshToken,
            user = new
            {
                userAccountId = account.Id,
                personId = account.PersonId,
                email = account.Email,
                role = account.Role.ToString(),
                firstName = account.Person.FirstName,
                lastName = account.Person.LastName,
                mfaEnabled = account.MfaEnabled
            }
        });
    }

    public record MfaSetupRequest(string? MfaToken);

    [HttpPost("mfa/setup")]
    public async Task<IActionResult> MfaSetup([FromBody] MfaSetupRequest? request)
    {
        Guid? accountId = null;

        if (HttpContext.Items["Actor"] is AccessTokenClaims actor)
        {
            accountId = actor.UserAccountId;
        }
        else if (!string.IsNullOrWhiteSpace(request?.MfaToken))
        {
            accountId = _tokens.ValidateMfaChallengeToken(request.MfaToken);
        }

        if (!accountId.HasValue)
        {
            return Unauthorized(new { error = "Authentication or valid MFA challenge token required for setup." });
        }

        var account = await _db.UserAccounts.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == accountId.Value);
        if (account == null || !account.IsActive)
        {
            return Unauthorized(new { error = "Account not found or inactive." });
        }

        var secret = _totp.GenerateSecret();
        var qrCodeUri = _totp.GenerateQrCodeUri(account.Email, secret);

        await _audit.LogAsync("MFA_SETUP_INITIATED", "UserAccount", account.Id.ToString(), account.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: Request.Headers.UserAgent.ToString());

        return Ok(new
        {
            secret,
            qrCodeUri
        });
    }

    public record MfaEnrollRequest(string Secret, string Code, string? MfaToken);

    [HttpPost("mfa/enroll")]
    public async Task<IActionResult> MfaEnroll([FromBody] MfaEnrollRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Secret) || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "Secret and 6-digit verification code are required." });
        }

        Guid? accountId = null;
        var isLoginFlow = false;

        if (HttpContext.Items["Actor"] is AccessTokenClaims actor)
        {
            accountId = actor.UserAccountId;
        }
        else if (!string.IsNullOrWhiteSpace(request.MfaToken))
        {
            accountId = _tokens.ValidateMfaChallengeToken(request.MfaToken);
            isLoginFlow = true;
        }

        if (!accountId.HasValue)
        {
            return Unauthorized(new { error = "Authentication or valid MFA challenge token required." });
        }

        var account = await _db.UserAccounts.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == accountId.Value);
        if (account == null || !account.IsActive)
        {
            return Unauthorized(new { error = "Account not found or inactive." });
        }

        var isValid = _totp.VerifyTotpCode(request.Secret, request.Code);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        if (!isValid)
        {
            await _audit.LogAsync("MFA_FAILED", "UserAccount", account.Id.ToString(), account.Id,
                metadata: new { reason = "invalid_enrollment_code" }, ipAddress: ip, userAgent: userAgent);
            return BadRequest(new { error = "Invalid code. Check your authenticator app and try again." });
        }

        var rawRecoveryCodes = _totp.GenerateRecoveryCodes(8);
        var recoveryItems = rawRecoveryCodes.Select(c => new RecoveryCodeItem(_totp.HashRecoveryCode(c), false)).ToList();

        account.MfaSecret = request.Secret;
        account.MfaEnabled = true;
        account.MfaEnrolledAt = DateTime.UtcNow;
        account.MfaRecoveryCodesJson = JsonSerializer.Serialize(recoveryItems);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("MFA_ENROLLED", "UserAccount", account.Id.ToString(), account.Id, ipAddress: ip, userAgent: userAgent);

        if (isLoginFlow)
        {
            // Complete login session for user logging in during mandatory enrollment
            var sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var token = _tokens.GenerateAccessToken(account, account.PersonId, sid);
            var (session, refreshToken) = await _sessions.CreateSessionAsync(account.Id, sid, ip, userAgent);

            SetSessionCookie(token);

            await _audit.LogAsync("LOGIN_SUCCEEDED", "UserAccount", account.Id.ToString(), account.Id, metadata: new { sid }, ipAddress: ip, userAgent: userAgent);

            return Ok(new
            {
                success = true,
                recoveryCodes = rawRecoveryCodes,
                accessToken = token,
                refreshToken,
                user = new
                {
                    userAccountId = account.Id,
                    personId = account.PersonId,
                    email = account.Email,
                    role = account.Role.ToString(),
                    firstName = account.Person.FirstName,
                    lastName = account.Person.LastName,
                    mfaEnabled = true
                }
            });
        }

        return Ok(new
        {
            success = true,
            recoveryCodes = rawRecoveryCodes
        });
    }

    public record MfaVerifyRequest(string MfaToken, string Code);

    [HttpPost("mfa/verify")]
    public async Task<IActionResult> MfaVerify([FromBody] MfaVerifyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MfaToken) || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "MFA challenge token and verification code are required." });
        }

        var accountId = _tokens.ValidateMfaChallengeToken(request.MfaToken);
        if (!accountId.HasValue)
        {
            return Unauthorized(new { error = "MFA challenge expired or invalid. Please sign in again." });
        }

        var account = await _db.UserAccounts.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == accountId.Value);
        if (account == null || !account.IsActive)
        {
            return Unauthorized(new { error = "Account not found or inactive." });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        // 1. Try TOTP code
        var isTotpValid = !string.IsNullOrEmpty(account.MfaSecret) && _totp.VerifyTotpCode(account.MfaSecret, request.Code);
        var isRecoveryUsed = false;

        if (isTotpValid)
        {
            await _audit.LogAsync("MFA_VERIFIED", "UserAccount", account.Id.ToString(), account.Id, ipAddress: ip, userAgent: userAgent);
        }
        else
        {
            // 2. Try Recovery code
            isRecoveryUsed = _totp.VerifyAndConsumeRecoveryCode(account, request.Code);
            if (isRecoveryUsed)
            {
                await _db.SaveChangesAsync();
                await _audit.LogAsync("MFA_RECOVERY_CODE_USED", "UserAccount", account.Id.ToString(), account.Id, ipAddress: ip, userAgent: userAgent);
            }
            else
            {
                await _audit.LogAsync("MFA_FAILED", "UserAccount", account.Id.ToString(), account.Id,
                    metadata: new { reason = "invalid_totp_or_recovery_code" }, ipAddress: ip, userAgent: userAgent);
                return Unauthorized(new { error = "Invalid two-factor code or recovery code." });
            }
        }

        // Login complete
        var sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var token = _tokens.GenerateAccessToken(account, account.PersonId, sid);
        var (session, refreshToken) = await _sessions.CreateSessionAsync(account.Id, sid, ip, userAgent);

        SetSessionCookie(token);

        await _audit.LogAsync("LOGIN_SUCCEEDED", "UserAccount", account.Id.ToString(), account.Id, metadata: new { sid, recoveryCodeUsed = isRecoveryUsed }, ipAddress: ip, userAgent: userAgent);

        return Ok(new
        {
            accessToken = token,
            refreshToken,
            user = new
            {
                userAccountId = account.Id,
                personId = account.PersonId,
                email = account.Email,
                role = account.Role.ToString(),
                firstName = account.Person.FirstName,
                lastName = account.Person.LastName,
                mfaEnabled = account.MfaEnabled
            }
        });
    }

    public record MfaDisableRequest(string Password);

    [HttpPost("mfa/disable")]
    [RequireAuth]
    public async Task<IActionResult> MfaDisable([FromBody] MfaDisableRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;

        if (actor.Role != UserRole.PATIENT)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "MFA is mandatory for CLINICIAN and ADMIN accounts and cannot be disabled." });
        }

        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == actor.UserAccountId);
        if (account == null)
            return Unauthorized(new { error = "Account not found." });

        if (string.IsNullOrWhiteSpace(request.Password) || !_hasher.VerifyPassword(account.PasswordHash, request.Password))
        {
            return BadRequest(new { error = "Enter your current password to disable two-factor authentication." });
        }

        account.MfaEnabled = false;
        account.MfaSecret = null;
        account.MfaRecoveryCodesJson = null;
        account.MfaEnrolledAt = null;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("MFA_DISABLED", "UserAccount", account.Id.ToString(), account.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: Request.Headers.UserAgent.ToString());

        return Ok(new { ok = true, message = "Two-factor authentication disabled." });
    }

    public record RefreshRequest(string? RefreshToken);

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshRequest? request)
    {
        var token = request?.RefreshToken ?? Request.Cookies["mp_refresh"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { error = "Refresh token is required." });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var refreshed = await _sessions.RefreshSessionAsync(token, ip, userAgent);
        if (refreshed == null)
        {
            Response.Cookies.Delete("mp_session");
            return Unauthorized(new { error = "Invalid, expired, or idle refresh token. Please sign in again." });
        }

        var (newAccessToken, newRefreshToken, session) = refreshed.Value;

        SetSessionCookie(newAccessToken);

        await _audit.LogAsync("TOKEN_REFRESHED", "UserSession", session.Id.ToString(), session.UserAccountId,
            metadata: new { sid = session.Sid }, ipAddress: ip, userAgent: userAgent);

        return Ok(new
        {
            accessToken = newAccessToken,
            refreshToken = newRefreshToken,
            user = new
            {
                userAccountId = session.UserAccount.Id,
                personId = session.UserAccount.PersonId,
                email = session.UserAccount.Email,
                role = session.UserAccount.Role.ToString(),
                firstName = session.UserAccount.Person.FirstName,
                lastName = session.UserAccount.Person.LastName,
                mfaEnabled = session.UserAccount.MfaEnabled
            }
        });
    }

    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HttpPost("change-password")]
    [RequireAuth]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Enter your current and new passwords." });
        }

        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == actor.UserAccountId);
        if (account == null)
            return Unauthorized(new { error = "Account not found." });

        if (!_hasher.VerifyPassword(account.PasswordHash, request.CurrentPassword))
        {
            return BadRequest(new { error = "Current password is incorrect." });
        }

        var strength = _hasher.ValidatePasswordStrength(request.NewPassword);
        if (!strength.Valid)
        {
            return BadRequest(new { error = strength.Reason });
        }

        account.PasswordHash = _hasher.HashPassword(request.NewPassword);
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        // Revoke all active sessions for this user to enforce re-login
        await _sessions.RevokeAllSessionsForUserAsync(account.Id, "password_changed");

        await _audit.LogAsync("PASSWORD_CHANGED", "UserAccount", account.Id.ToString(), account.Id, ipAddress: ip, userAgent: userAgent);
        await _audit.LogAsync("SESSIONS_REVOKED", "UserAccount", account.Id.ToString(), account.Id,
            metadata: new { reason = "password_changed" }, ipAddress: ip, userAgent: userAgent);

        Response.Cookies.Delete("mp_session");

        return Ok(new { ok = true, message = "Password updated successfully. Please sign in again." });
    }

    public record RegisterRequest(string Email, string Password, string FirstName, string LastName, string DateOfBirth, string? Phone);

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.DateOfBirth))
        {
            return BadRequest(new { error = "Please check the highlighted fields." });
        }

        var strength = _hasher.ValidatePasswordStrength(request.Password);
        if (!strength.Valid)
            return BadRequest(new { error = strength.Reason });

        if (!DateTime.TryParse(request.DateOfBirth, out var dob) || dob > DateTime.UtcNow || dob < DateTime.UtcNow.AddYears(-130))
            return BadRequest(new { error = "Enter a valid date of birth." });

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _db.UserAccounts.AnyAsync(u => u.Email == normalizedEmail))
            return Conflict(new { error = "An account with that email already exists." });

        var person = new Person
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = DateTime.SpecifyKind(dob.Date, DateTimeKind.Utc),
            Email = normalizedEmail,
            Phone = request.Phone?.Trim()
        };
        _db.Persons.Add(person);

        var account = new UserAccount
        {
            Person = person,
            Email = normalizedEmail,
            PasswordHash = _hasher.HashPassword(request.Password),
            Role = UserRole.PATIENT
        };
        _db.UserAccounts.Add(account);

        var mrn = await GenerateUniqueMrnAsync();
        var patient = new Patient
        {
            Person = person,
            Mrn = mrn
        };
        _db.Patients.Add(patient);

        await _db.SaveChangesAsync();

        var sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var token = _tokens.GenerateAccessToken(account, person.Id, sid);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var (session, refreshToken) = await _sessions.CreateSessionAsync(account.Id, sid, ip, userAgent);

        SetSessionCookie(token);

        await _audit.LogAsync("REGISTER", "UserAccount", account.Id.ToString(), account.Id, metadata: new { sid }, ipAddress: ip, userAgent: userAgent);

        return StatusCode(201, new
        {
            accessToken = token,
            refreshToken,
            user = new
            {
                userAccountId = account.Id,
                personId = person.Id,
                email = account.Email,
                role = account.Role.ToString(),
                firstName = person.FirstName,
                lastName = person.LastName,
                mfaEnabled = false
            }
        });
    }

    [HttpPost("logout")]
    [RequireAuth]
    public async Task<IActionResult> Logout()
    {
        if (HttpContext.Items["Actor"] is AccessTokenClaims actor)
        {
            await _sessions.RevokeSessionAsync(actor.Sid, "logout");
            await _audit.LogAsync("LOGOUT", "UserAccount", actor.UserAccountId.ToString(), actor.UserAccountId,
                metadata: new { sid = actor.Sid },
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString());
        }

        Response.Cookies.Delete("mp_session");
        return Ok(new { ok = true });
    }

    [HttpGet("me")]
    [RequireAuth]
    public async Task<IActionResult> Me()
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;

        var account = await _db.UserAccounts
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == actor.UserAccountId);

        if (account == null)
        {
            Response.Cookies.Delete("mp_session");
            return Unauthorized(new { error = "Account no longer exists." });
        }

        var patientIds = await _accessControl.ListAccessiblePatientsAsync(actor);

        var patients = await _db.Patients
            .AsNoTracking()
            .Include(p => p.Person)
            .Include(p => p.Snapshot)
            .Where(p => patientIds.Contains(p.Id))
            .ToListAsync();

        var managing = new List<object>();

        foreach (var p in patients)
        {
            var isSelf = p.PersonId == actor.PersonId;
            var canPayBills = isSelf || (await _accessControl.ResolveAccessAsync(actor, p.Id, Permission.payBills)).Allowed;

            managing.Add(new
            {
                patientId = p.Id,
                firstName = p.Person.FirstName,
                lastName = p.Person.LastName,
                dateOfBirth = ClinicalFormatting.ToIsoDate(p.Person.DateOfBirth),
                age = ClinicalFormatting.AgeFrom(p.Person.DateOfBirth),
                mrn = p.Mrn,
                isSelf,
                relationship = isSelf ? "self" : "dependent",
                outstandingBalanceCents = canPayBills ? p.Snapshot?.OutstandingBalanceCents ?? 0 : (int?)null
            });
        }

        return Ok(new
        {
            user = new
            {
                userAccountId = account.Id,
                personId = account.PersonId,
                email = account.Email,
                role = account.Role.ToString(),
                firstName = account.Person.FirstName,
                lastName = account.Person.LastName,
                mfaEnabled = account.MfaEnabled
            },
            managing
        });
    }

    private void SetSessionCookie(string token)
    {
        Response.Cookies.Append("mp_session", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(_tokens.ExpirationMinutes),
            Path = "/"
        });
    }

    private async Task<string> GenerateUniqueMrnAsync()
    {
        for (int i = 0; i < 5; i++)
        {
            var candidate = $"MRN-{RandomNumberGenerator.GetInt32(1000000, 9999999)}";
            if (!await _db.Patients.AnyAsync(p => p.Mrn == candidate))
                return candidate;
        }
        return $"MRN-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }
}
