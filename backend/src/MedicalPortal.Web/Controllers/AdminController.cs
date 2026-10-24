using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/admin")]
[RequireAuth]
[RequireAdmin]
[EnableRateLimiting("GeneralApiPolicy")]
public class AdminController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionService _sessions;
    private readonly IAuditService _audit;

    public AdminController(
        MedicalPortalDbContext db,
        IPasswordHasher hasher,
        ISessionService sessions,
        IAuditService audit)
    {
        _db = db;
        _hasher = hasher;
        _sessions = sessions;
        _audit = audit;
    }

    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string UserAgent => Request.Headers.UserAgent.ToString();

    private bool IsFullAdmin => Actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN;

    // ==========================================
    // 1. Roles Metadata
    // ==========================================

    [HttpGet("roles")]
    public IActionResult GetAvailableRoles()
    {
        var roles = Enum.GetValues<UserRole>()
            .Select(r => new
            {
                name = r.ToString(),
                value = (int)r,
                isStaff = r != UserRole.PATIENT,
                isAdmin = r is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.DEPT_ADMIN
            });

        return Ok(roles);
    }

    // ==========================================
    // 2. User Management
    // ==========================================

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] string? search = null,
        [FromQuery] UserRole? role = null,
        [FromQuery] bool? active = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.UserAccounts
            .AsNoTracking()
            .Include(u => u.Person)
                .ThenInclude(p => p.Patient)
            .Include(u => u.Memberships)
                .ThenInclude(m => m.Facility)
            .Include(u => u.Memberships)
                .ThenInclude(m => m.Department)
            .AsQueryable();

        if (role.HasValue)
        {
            query = query.Where(u => u.Role == role.Value);
        }

        if (active.HasValue)
        {
            query = query.Where(u => u.IsActive == active.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(s) ||
                u.Person.FirstName.ToLower().Contains(s) ||
                u.Person.LastName.ToLower().Contains(s) ||
                (u.Person.Patient != null && u.Person.Patient.Mrn.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            totalCount,
            totalPages,
            items = users.Select(u => new
            {
                id = u.Id,
                email = u.Email,
                role = u.Role.ToString(),
                isActive = u.IsActive,
                isLocked = u.LockedUntil.HasValue && u.LockedUntil > DateTime.UtcNow,
                lockedUntil = u.LockedUntil?.ToString("o"),
                mfaEnabled = u.MfaEnabled,
                lastLoginAt = u.LastLoginAt?.ToString("o"),
                createdAt = u.CreatedAt.ToString("o"),
                person = new
                {
                    id = u.PersonId,
                    firstName = u.Person.FirstName,
                    lastName = u.Person.LastName,
                    fullName = $"{u.Person.FirstName} {u.Person.LastName}".Trim(),
                    dateOfBirth = u.Person.DateOfBirth.ToString("yyyy-MM-dd"),
                    mrn = u.Person.Patient?.Mrn
                },
                memberships = u.Memberships.Select(m => new
                {
                    id = m.Id,
                    facilityId = m.FacilityId,
                    facilityName = m.Facility?.Name,
                    facilityCode = m.Facility?.Code,
                    departmentId = m.DepartmentId,
                    departmentName = m.Department?.Name,
                    departmentCode = m.Department?.Code,
                    role = m.Role,
                    createdAt = m.CreatedAt.ToString("o")
                })
            })
        });
    }

    public record CreateUserRequest(
        string Email,
        string Password,
        string Role,
        string FirstName,
        string LastName,
        string DateOfBirth,
        Guid? FacilityId = null,
        Guid? DepartmentId = null,
        string? MembershipRole = null);

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Creating users requires an administrator role." });

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            return BadRequest(new { error = "Valid email address is required." });

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters long." });

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return BadRequest(new { error = $"Invalid role '{request.Role}'." });

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new { error = "First and last name are required." });

        if (!DateTime.TryParse(request.DateOfBirth, out var dob))
            return BadRequest(new { error = "Valid date of birth (YYYY-MM-DD) is required." });

        dob = DateTime.SpecifyKind(dob, DateTimeKind.Utc);
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existing = await _db.UserAccounts.AnyAsync(u => u.Email == normalizedEmail);
        if (existing)
            return Conflict(new { error = "An account with this email address already exists." });

        var person = new Person
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = normalizedEmail,
            DateOfBirth = dob,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Persons.Add(person);

        var account = new UserAccount
        {
            Person = person,
            Email = normalizedEmail,
            PasswordHash = _hasher.HashPassword(request.Password),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.UserAccounts.Add(account);

        // If PATIENT, create Patient record
        if (role == UserRole.PATIENT)
        {
            var mrn = $"MRN-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
            var patient = new Patient
            {
                Person = person,
                Mrn = mrn,
                CreatedAt = DateTime.UtcNow
            };
            _db.Patients.Add(patient);
        }
        else if (role is UserRole.CLINICIAN or UserRole.PHYSICIAN)
        {
            var provider = new Provider
            {
                AccountId = account.Id,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Credentials = role == UserRole.PHYSICIAN ? "MD" : "PA-C",
                Specialty = "General Practice",
                CreatedAt = DateTime.UtcNow
            };
            _db.Providers.Add(provider);

            if (request.DepartmentId.HasValue)
            {
                _db.ProviderDepartments.Add(new ProviderDepartment
                {
                    Provider = provider,
                    DepartmentId = request.DepartmentId.Value,
                    IsPrimary = true
                });
            }
        }

        // Optional initial membership
        if (request.FacilityId.HasValue || request.DepartmentId.HasValue)
        {
            var membership = new UserMembership
            {
                UserAccount = account,
                FacilityId = request.FacilityId,
                DepartmentId = request.DepartmentId,
                Role = request.MembershipRole ?? role.ToString(),
                CreatedAt = DateTime.UtcNow
            };
            _db.UserMemberships.Add(membership);
        }

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_USER_CREATED",
            resourceType: "UserAccount",
            resourceId: account.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new
            {
                email = account.Email,
                role = account.Role.ToString(),
                facilityId = request.FacilityId,
                departmentId = request.DepartmentId
            },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return StatusCode(201, new
        {
            id = account.Id,
            email = account.Email,
            role = account.Role.ToString(),
            isActive = account.IsActive,
            message = "User created successfully."
        });
    }

    [HttpPost("users/{id}/disable")]
    public async Task<IActionResult> DisableUser(Guid id)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Disabling users requires an administrator role." });

        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id);
        if (account == null)
            return NotFound(new { error = "User account not found." });

        if (account.Id == Actor.UserAccountId)
            return BadRequest(new { error = "Administrators cannot disable their own account." });

        account.IsActive = false;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _sessions.RevokeAllSessionsForUserAsync(account.Id, "admin_disabled");

        await _audit.LogAsync(
            action: "ADMIN_USER_DISABLED",
            resourceType: "UserAccount",
            resourceId: account.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { email = account.Email, role = account.Role.ToString() },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return Ok(new { ok = true, message = $"User '{account.Email}' has been disabled and all sessions revoked." });
    }

    [HttpPost("users/{id}/enable")]
    public async Task<IActionResult> EnableUser(Guid id)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Enabling users requires an administrator role." });

        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id);
        if (account == null)
            return NotFound(new { error = "User account not found." });

        account.IsActive = true;
        account.LockedUntil = null;
        account.FailedLoginCount = 0;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_USER_ENABLED",
            resourceType: "UserAccount",
            resourceId: account.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { email = account.Email, role = account.Role.ToString() },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return Ok(new { ok = true, message = $"User '{account.Email}' has been re-enabled." });
    }

    public record UpdateRoleRequest(string Role);

    [HttpPut("users/{id}/role")]
    public async Task<IActionResult> UpdateUserRole(Guid id, [FromBody] UpdateRoleRequest request)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Updating roles requires an administrator role." });

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return BadRequest(new { error = $"Invalid role '{request.Role}'." });

        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id);
        if (account == null)
            return NotFound(new { error = "User account not found." });

        if (account.Id == Actor.UserAccountId && role != UserRole.ADMIN && role != UserRole.SYSTEM_ADMIN)
            return BadRequest(new { error = "Administrators cannot remove their own administrator privileges." });

        var oldRole = account.Role;
        account.Role = role;
        account.AuthorizationVersion++;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _sessions.RevokeAllSessionsForUserAsync(account.Id, $"role_changed_to_{role}");

        await _audit.LogAsync(
            action: "ADMIN_USER_ROLE_UPDATED",
            resourceType: "UserAccount",
            resourceId: account.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { email = account.Email, oldRole = oldRole.ToString(), newRole = request.Role.ToString() },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return Ok(new { ok = true, role = account.Role.ToString(), message = $"Role updated to {account.Role}." });
    }

    public record AddMembershipRequest(Guid? FacilityId, Guid? DepartmentId, string? Role);

    [HttpPost("users/{id}/memberships")]
    public async Task<IActionResult> AddUserMembership(Guid id, [FromBody] AddMembershipRequest request)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Assigning memberships requires an administrator role." });

        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id);
        if (account == null)
            return NotFound(new { error = "User account not found." });

        if (!request.FacilityId.HasValue && !request.DepartmentId.HasValue)
            return BadRequest(new { error = "Either FacilityId or DepartmentId must be specified." });

        Guid? facilityId = request.FacilityId;
        if (request.DepartmentId.HasValue)
        {
            var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value);
            if (dept == null)
                return NotFound(new { error = "Department not found." });
            facilityId ??= dept.FacilityId;
        }

        var membership = new UserMembership
        {
            UserAccountId = account.Id,
            FacilityId = facilityId,
            DepartmentId = request.DepartmentId,
            Role = request.Role ?? account.Role.ToString(),
            CreatedAt = DateTime.UtcNow
        };
        _db.UserMemberships.Add(membership);

        // If user has provider record and department is specified, also add ProviderDepartment
        var provider = await _db.Providers.FirstOrDefaultAsync(p => p.AccountId == account.Id);
        if (provider != null && request.DepartmentId.HasValue)
        {
            var exists = await _db.ProviderDepartments.AnyAsync(pd => pd.ProviderId == provider.Id && pd.DepartmentId == request.DepartmentId.Value);
            if (!exists)
            {
                _db.ProviderDepartments.Add(new ProviderDepartment
                {
                    ProviderId = provider.Id,
                    DepartmentId = request.DepartmentId.Value,
                    IsPrimary = false,
                    AssignedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_MEMBERSHIP_ADDED",
            resourceType: "UserMembership",
            resourceId: membership.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new
            {
                targetUserId = account.Id,
                facilityId,
                departmentId = request.DepartmentId,
                role = membership.Role
            },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return StatusCode(201, new
        {
            id = membership.Id,
            facilityId = membership.FacilityId,
            departmentId = membership.DepartmentId,
            role = membership.Role,
            message = "Membership added successfully."
        });
    }

    [HttpDelete("users/{id}/memberships/{membershipId}")]
    public async Task<IActionResult> RemoveUserMembership(Guid id, Guid membershipId)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Removing memberships requires an administrator role." });

        var membership = await _db.UserMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.UserAccountId == id);

        if (membership == null)
            return NotFound(new { error = "Membership not found." });

        _db.UserMemberships.Remove(membership);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_MEMBERSHIP_REMOVED",
            resourceType: "UserMembership",
            resourceId: membershipId.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { targetUserId = id, membershipId },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return Ok(new { ok = true, message = "Membership removed." });
    }

    // ==========================================
    // 3. Facility and Department Management
    // ==========================================

    [HttpGet("facilities")]
    public async Task<IActionResult> ListFacilities()
    {
        var facilities = await _db.Facilities
            .AsNoTracking()
            .Include(f => f.Organization)
            .Include(f => f.Departments)
            .OrderBy(f => f.Name)
            .ToListAsync();

        return Ok(facilities.Select(f => new
        {
            id = f.Id,
            organizationId = f.OrganizationId,
            organizationName = f.Organization.Name,
            name = f.Name,
            code = f.Code,
            addressLine1 = f.AddressLine1,
            addressLine2 = f.AddressLine2,
            city = f.City,
            state = f.State,
            postalCode = f.PostalCode,
            phone = f.Phone,
            createdAt = f.CreatedAt.ToString("o"),
            departments = f.Departments.OrderBy(d => d.Name).Select(d => new
            {
                id = d.Id,
                facilityId = d.FacilityId,
                name = d.Name,
                code = d.Code,
                specialty = d.Specialty,
                phone = d.Phone,
                createdAt = d.CreatedAt.ToString("o")
            })
        }));
    }

    public record CreateFacilityRequest(
        Guid? OrganizationId,
        string Name,
        string Code,
        string? AddressLine1 = null,
        string? AddressLine2 = null,
        string? City = null,
        string? State = null,
        string? PostalCode = null,
        string? Phone = null);

    [HttpPost("facilities")]
    public async Task<IActionResult> CreateFacility([FromBody] CreateFacilityRequest request)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Managing facilities requires an administrator role." });

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Facility name and code are required." });

        var orgId = request.OrganizationId;
        if (!orgId.HasValue)
        {
            var firstOrg = await _db.Organizations.FirstOrDefaultAsync();
            if (firstOrg == null)
            {
                firstOrg = new Organization { Name = "Main Health System", Code = "MHS" };
                _db.Organizations.Add(firstOrg);
                await _db.SaveChangesAsync();
            }
            orgId = firstOrg.Id;
        }

        var facility = new Facility
        {
            OrganizationId = orgId.Value,
            Name = request.Name.Trim(),
            Code = request.Code.Trim().ToUpperInvariant(),
            AddressLine1 = request.AddressLine1?.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            City = request.City?.Trim(),
            State = request.State?.Trim(),
            PostalCode = request.PostalCode?.Trim(),
            Phone = request.Phone?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.Facilities.Add(facility);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_FACILITY_CREATED",
            resourceType: "Facility",
            resourceId: facility.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { name = facility.Name, code = facility.Code },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return StatusCode(201, new
        {
            id = facility.Id,
            name = facility.Name,
            code = facility.Code,
            message = "Facility created successfully."
        });
    }

    public record UpdateFacilityRequest(
        string Name,
        string Code,
        string? AddressLine1 = null,
        string? AddressLine2 = null,
        string? City = null,
        string? State = null,
        string? PostalCode = null,
        string? Phone = null);

    [HttpPut("facilities/{id}")]
    public async Task<IActionResult> UpdateFacility(Guid id, [FromBody] UpdateFacilityRequest request)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Managing facilities requires an administrator role." });

        var facility = await _db.Facilities.FirstOrDefaultAsync(f => f.Id == id);
        if (facility == null)
            return NotFound(new { error = "Facility not found." });

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Facility name and code are required." });

        facility.Name = request.Name.Trim();
        facility.Code = request.Code.Trim().ToUpperInvariant();
        facility.AddressLine1 = request.AddressLine1?.Trim();
        facility.AddressLine2 = request.AddressLine2?.Trim();
        facility.City = request.City?.Trim();
        facility.State = request.State?.Trim();
        facility.PostalCode = request.PostalCode?.Trim();
        facility.Phone = request.Phone?.Trim();

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_FACILITY_UPDATED",
            resourceType: "Facility",
            resourceId: facility.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { name = facility.Name, code = facility.Code },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return Ok(new { ok = true, message = "Facility updated." });
    }

    public record CreateDepartmentRequest(
        Guid FacilityId,
        string Name,
        string Code,
        string? Specialty = null,
        string? Phone = null);

    [HttpPost("departments")]
    public async Task<IActionResult> CreateDepartment([FromBody] CreateDepartmentRequest request)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Managing departments requires an administrator role." });

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Department name and code are required." });

        var facility = await _db.Facilities.FirstOrDefaultAsync(f => f.Id == request.FacilityId);
        if (facility == null)
            return NotFound(new { error = "Facility not found." });

        var dept = new Department
        {
            FacilityId = request.FacilityId,
            Name = request.Name.Trim(),
            Code = request.Code.Trim().ToUpperInvariant(),
            Specialty = request.Specialty?.Trim(),
            Phone = request.Phone?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_DEPARTMENT_CREATED",
            resourceType: "Department",
            resourceId: dept.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { name = dept.Name, code = dept.Code, facilityId = dept.FacilityId },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return StatusCode(201, new
        {
            id = dept.Id,
            name = dept.Name,
            code = dept.Code,
            specialty = dept.Specialty,
            facilityId = dept.FacilityId,
            message = "Department created successfully."
        });
    }

    public record UpdateDepartmentRequest(
        string Name,
        string Code,
        string? Specialty = null,
        string? Phone = null);

    [HttpPut("departments/{id}")]
    public async Task<IActionResult> UpdateDepartment(Guid id, [FromBody] UpdateDepartmentRequest request)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Managing departments requires an administrator role." });

        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null)
            return NotFound(new { error = "Department not found." });

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Department name and code are required." });

        dept.Name = request.Name.Trim();
        dept.Code = request.Code.Trim().ToUpperInvariant();
        dept.Specialty = request.Specialty?.Trim();
        dept.Phone = request.Phone?.Trim();

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_DEPARTMENT_UPDATED",
            resourceType: "Department",
            resourceId: dept.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { name = dept.Name, code = dept.Code },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return Ok(new { ok = true, message = "Department updated." });
    }

    // ==========================================
    // 4. Proxy Grants View & Revocation
    // ==========================================

    [HttpGet("proxy-grants")]
    public async Task<IActionResult> ListProxyGrants()
    {
        var grants = await _db.ProxyGrants
            .AsNoTracking()
            .Include(g => g.Grantor)
                .ThenInclude(p => p.Patient)
            .Include(g => g.Grantee)
            .OrderByDescending(g => g.ConsentGivenAt)
            .ToListAsync();

        var now = DateTime.UtcNow;

        return Ok(grants.Select(g => new
        {
            id = g.Id,
            grantor = new
            {
                id = g.GrantorId,
                name = $"{g.Grantor.FirstName} {g.Grantor.LastName}".Trim(),
                email = g.Grantor.Email,
                mrn = g.Grantor.Patient?.Mrn
            },
            grantee = new
            {
                id = g.GranteeId,
                name = $"{g.Grantee.FirstName} {g.Grantee.LastName}".Trim(),
                email = g.Grantee.Email
            },
            permissions = new
            {
                canViewRecords = g.CanViewRecords,
                canMessage = g.CanMessage,
                canManageAppointments = g.CanManageAppointments,
                canRequestRefills = g.CanRequestRefills,
                canPayBills = g.CanPayBills,
                canManageSharing = g.CanManageSharing
            },
            consentGivenAt = g.ConsentGivenAt.ToString("o"),
            consentExpiresAt = g.ConsentExpiresAt?.ToString("o"),
            revokedAt = g.RevokedAt?.ToString("o"),
            isActive = g.RevokedAt == null && (g.ConsentExpiresAt == null || g.ConsentExpiresAt > now)
        }));
    }

    [HttpPost("proxy-grants/{id}/revoke")]
    public async Task<IActionResult> RevokeProxyGrant(Guid id)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Revoking proxy grants requires an administrator role." });

        var grant = await _db.ProxyGrants.FirstOrDefaultAsync(g => g.Id == id);
        if (grant == null)
            return NotFound(new { error = "Proxy grant not found." });

        if (grant.RevokedAt != null)
            return BadRequest(new { error = "Proxy grant is already revoked." });

        grant.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_PROXY_GRANT_REVOKED",
            resourceType: "ProxyGrant",
            resourceId: grant.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            metadata: new { grantorId = grant.GrantorId, granteeId = grant.GranteeId },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return Ok(new { ok = true, message = "Proxy grant revoked successfully." });
    }

    // ==========================================
    // 5. Active Shares View & Revocation
    // ==========================================

    [HttpGet("shares")]
    public async Task<IActionResult> ListActiveShares()
    {
        var shares = await _db.RecordShares
            .AsNoTracking()
            .Include(s => s.Patient)
                .ThenInclude(p => p.Person)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var now = DateTime.UtcNow;

        return Ok(shares.Select(s => new
        {
            id = s.Id,
            patientId = s.PatientId,
            patientName = $"{s.Patient.Person.FirstName} {s.Patient.Person.LastName}".Trim(),
            mrn = s.Patient.Mrn,
            label = s.Label,
            recipientName = s.RecipientName,
            recipientRole = s.RecipientRole,
            scopeJson = s.ScopeJson,
            maxUses = s.MaxUses,
            useCount = s.UseCount,
            createdAt = s.CreatedAt.ToString("o"),
            expiresAt = s.ExpiresAt.ToString("o"),
            revokedAt = s.RevokedAt?.ToString("o"),
            isActive = s.RevokedAt == null && s.ExpiresAt > now && s.UseCount < s.MaxUses
        }));
    }

    [HttpPost("shares/{id}/revoke")]
    public async Task<IActionResult> RevokeShare(Guid id)
    {
        if (!IsFullAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Revoking record shares requires an administrator role." });

        var share = await _db.RecordShares.FirstOrDefaultAsync(s => s.Id == id);
        if (share == null)
            return NotFound(new { error = "Record share not found." });

        if (share.RevokedAt != null)
            return BadRequest(new { error = "Record share is already revoked." });

        share.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "ADMIN_RECORD_SHARE_REVOKED",
            resourceType: "RecordShare",
            resourceId: share.Id.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            patientId: share.PatientId,
            metadata: new { label = share.Label, recipient = share.RecipientName },
            ipAddress: ClientIp,
            userAgent: UserAgent
        );

        return Ok(new { ok = true, message = "Record share revoked successfully." });
    }

    // ==========================================
    // 6. Audit Log View & Hash Verification
    // ==========================================

    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? patientId = null,
        [FromQuery] Guid? actorId = null,
        [FromQuery] string? resourceType = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool? isProxy = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (patientId.HasValue) query = query.Where(a => a.PatientId == patientId.Value);
        if (actorId.HasValue) query = query.Where(a => a.ActorUserAccountId == actorId.Value);

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            var rType = resourceType.Trim().ToLower();
            query = query.Where(a => a.ResourceType.ToLower() == rType);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var act = action.Trim().ToLower();
            query = query.Where(a => a.Action.ToLower() == act);
        }

        if (from.HasValue) query = query.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(a => a.CreatedAt <= to.Value);
        if (isProxy.HasValue) query = query.Where(a => a.IsProxy == isProxy.Value);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var logs = await query
            .OrderByDescending(a => a.SequenceNumber)
            .ThenByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var actorIds = logs
            .Where(l => l.ActorUserAccountId.HasValue)
            .Select(l => l.ActorUserAccountId!.Value)
            .Distinct()
            .ToList();

        var actors = await _db.UserAccounts
            .AsNoTracking()
            .Include(u => u.Person)
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        var items = logs.Select(l =>
        {
            var actor = l.ActorUserAccountId.HasValue ? actors.GetValueOrDefault(l.ActorUserAccountId.Value) : null;
            var actorName = actor?.Person != null ? $"{actor.Person.FirstName} {actor.Person.LastName}".Trim() : "System";

            return new
            {
                id = l.Id,
                action = l.Action,
                resourceType = l.ResourceType,
                resourceId = l.ResourceId,
                actor = new
                {
                    id = l.ActorUserAccountId,
                    name = actorName,
                    email = actor?.Email,
                    role = actor?.Role.ToString() ?? "SYSTEM"
                },
                patientId = l.PatientId,
                actingForPersonId = l.ActingForPersonId,
                isProxy = l.IsProxy,
                ipAddress = l.IpAddress,
                userAgent = l.UserAgent,
                createdAt = l.CreatedAt.ToString("o"),
                sequenceNumber = l.SequenceNumber,
                previousHash = l.PreviousHash,
                hash = l.Hash,
                metadataJson = l.MetadataJson
            };
        });

        return Ok(new
        {
            page,
            pageSize,
            totalCount,
            totalPages,
            items
        });
    }

    [HttpGet("audit-logs/verify")]
    public async Task<IActionResult> VerifyAuditChain()
    {
        var logs = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => !string.IsNullOrEmpty(a.Hash))
            .OrderBy(a => a.SequenceNumber)
            .ToListAsync();

        var isValid = AuditLogHasher.VerifyIntegrity(logs, out var corruptedId, out var reason);

        return Ok(new
        {
            valid = isValid,
            totalRecords = logs.Count,
            genesisHash = AuditLogHasher.GenesisHash,
            latestHash = logs.LastOrDefault()?.Hash,
            corruptedLogId = corruptedId,
            error = reason
        });
    }
}
