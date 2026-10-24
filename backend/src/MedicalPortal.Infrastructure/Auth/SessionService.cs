using System.Security.Cryptography;
using System.Text;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MedicalPortal.Infrastructure.Auth;

public record SessionValidationResult(bool IsValid, string? FailureReason, UserSession? Session, UserAccount? Account);

public interface ISessionService
{
    Task<(UserSession Session, string RefreshToken)> CreateSessionAsync(
        Guid userAccountId,
        string sid,
        string? ipAddress,
        string? userAgent,
        string? customRefreshToken = null);

    Task<SessionValidationResult> ValidateSessionAsync(
        string sid,
        Guid? expectedUserAccountId = null,
        int? expectedAuthVersion = null,
        UserRole? expectedRole = null);

    Task RevokeSessionAsync(string sid, string reason);

    Task RevokeAllSessionsForUserAsync(Guid userAccountId, string reason);

    Task<(string AccessToken, string RefreshToken, UserSession Session)?> RefreshSessionAsync(
        string refreshToken,
        string? ipAddress,
        string? userAgent);

    int IdleTimeoutMinutes { get; }
    int RefreshTokenExpirationDays { get; }
}

public class SessionService : ISessionService
{
    private readonly MedicalPortalDbContext _db;
    private readonly ITokenService _tokens;
    public int IdleTimeoutMinutes { get; }
    public int RefreshTokenExpirationDays { get; }

    public SessionService(MedicalPortalDbContext db, ITokenService tokens, IConfiguration configuration)
    {
        _db = db;
        _tokens = tokens;

        if (int.TryParse(configuration["Session:IdleTimeoutMinutes"] ?? configuration["SESSION_IDLE_TIMEOUT_MINUTES"], out var idle) && idle > 0)
        {
            IdleTimeoutMinutes = idle;
        }
        else
        {
            IdleTimeoutMinutes = 15; // 15 minutes default HIPAA-compliant idle timeout
        }

        if (int.TryParse(configuration["Session:RefreshTokenExpirationDays"] ?? configuration["SESSION_REFRESH_TOKEN_DAYS"], out var days) && days > 0)
        {
            RefreshTokenExpirationDays = days;
        }
        else
        {
            RefreshTokenExpirationDays = 7; // 7 days default refresh token lifetime
        }
    }

    public async Task<(UserSession Session, string RefreshToken)> CreateSessionAsync(
        Guid userAccountId,
        string sid,
        string? ipAddress,
        string? userAgent,
        string? customRefreshToken = null)
    {
        var refreshToken = customRefreshToken ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var refreshTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

        var session = new UserSession
        {
            Sid = sid,
            UserAccountId = userAccountId,
            RefreshTokenHash = refreshTokenHash,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenExpirationDays),
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync();

        return (session, refreshToken);
    }

    public async Task<SessionValidationResult> ValidateSessionAsync(
        string sid,
        Guid? expectedUserAccountId = null,
        int? expectedAuthVersion = null,
        UserRole? expectedRole = null)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return new SessionValidationResult(false, "Session identifier is missing.", null, null);
        }

        var session = await _db.UserSessions
            .Include(s => s.UserAccount).ThenInclude(u => u.Person)
            .FirstOrDefaultAsync(s => s.Sid == sid);

        var now = DateTime.UtcNow;

        if (session == null)
        {
            // SEC-01 & SEC-14: Lazy session creation is strictly deleted.
            // Unknown or forged session identifiers are immediately rejected.
            return new SessionValidationResult(false, "Session not found or expired.", null, null);
        }

        if (session.RevokedAt.HasValue)
        {
            return new SessionValidationResult(false, "Session has been revoked. Please sign in again.", session, session.UserAccount);
        }

        if (!session.UserAccount.IsActive)
        {
            session.RevokedAt = now;
            session.RevokedReason = "account_disabled";
            await _db.SaveChangesAsync();
            return new SessionValidationResult(false, "Account has been deactivated.", session, session.UserAccount);
        }

        // Reconcile user account ID
        if (expectedUserAccountId.HasValue && session.UserAccountId != expectedUserAccountId.Value)
        {
            session.RevokedAt = now;
            session.RevokedReason = "account_mismatch";
            await _db.SaveChangesAsync();
            return new SessionValidationResult(false, "Session account mismatch.", session, session.UserAccount);
        }

        // Reconcile user role against database (prevent forged token role privilege escalation)
        if (expectedRole.HasValue && session.UserAccount.Role != expectedRole.Value)
        {
            session.RevokedAt = now;
            session.RevokedReason = "role_mismatch";
            await _db.SaveChangesAsync();
            return new SessionValidationResult(false, "Session role mismatch. Permissions have changed.", session, session.UserAccount);
        }

        // Reconcile authorization version (immediate revocation on role/permission changes)
        if (expectedAuthVersion.HasValue && session.UserAccount.AuthorizationVersion != expectedAuthVersion.Value)
        {
            session.RevokedAt = now;
            session.RevokedReason = "auth_version_mismatch";
            await _db.SaveChangesAsync();
            return new SessionValidationResult(false, "Session permissions have expired. Please sign in again.", session, session.UserAccount);
        }

        if (session.ExpiresAt < now)
        {
            session.RevokedAt = now;
            session.RevokedReason = "expired";
            await _db.SaveChangesAsync();
            return new SessionValidationResult(false, "Session has expired. Please sign in again.", session, session.UserAccount);
        }

        // Enforce Idle Timeout
        if (now - session.LastActiveAt > TimeSpan.FromMinutes(IdleTimeoutMinutes))
        {
            session.RevokedAt = now;
            session.RevokedReason = "idle_timeout";
            await _db.SaveChangesAsync();
            return new SessionValidationResult(false, "Session expired due to inactivity. Please sign in again.", session, session.UserAccount);
        }

        // Update LastActiveAt to keep session alive during activity
        session.LastActiveAt = now;
        await _db.SaveChangesAsync();

        return new SessionValidationResult(true, null, session, session.UserAccount);
    }

    public async Task RevokeSessionAsync(string sid, string reason)
    {
        if (string.IsNullOrWhiteSpace(sid)) return;

        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Sid == sid);
        if (session != null && !session.RevokedAt.HasValue)
        {
            session.RevokedAt = DateTime.UtcNow;
            session.RevokedReason = reason;
            await _db.SaveChangesAsync();
        }
    }

    public async Task RevokeAllSessionsForUserAsync(Guid userAccountId, string reason)
    {
        var now = DateTime.UtcNow;
        await _db.UserSessions
            .Where(s => s.UserAccountId == userAccountId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, reason));
    }

    public async Task<(string AccessToken, string RefreshToken, UserSession Session)?> RefreshSessionAsync(
        string refreshToken,
        string? ipAddress,
        string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken.Trim())));

        var session = await _db.UserSessions
            .Include(s => s.UserAccount).ThenInclude(u => u.Person)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash);

        if (session == null) return null;

        var now = DateTime.UtcNow;

        if (session.RevokedAt.HasValue || !session.UserAccount.IsActive || session.ExpiresAt < now)
        {
            return null;
        }

        // Check idle timeout
        if (now - session.LastActiveAt > TimeSpan.FromMinutes(IdleTimeoutMinutes))
        {
            session.RevokedAt = now;
            session.RevokedReason = "idle_timeout";
            await _db.SaveChangesAsync();
            return null;
        }

        // Rotate refresh token
        var newRefreshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        session.RefreshTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newRefreshToken)));
        session.LastActiveAt = now;
        session.IpAddress = ipAddress ?? session.IpAddress;
        session.UserAgent = userAgent ?? session.UserAgent;

        await _db.SaveChangesAsync();

        var newAccessToken = _tokens.GenerateAccessToken(session.UserAccount, session.UserAccount.PersonId, session.Sid);

        return (newAccessToken, newRefreshToken, session);
    }
}
