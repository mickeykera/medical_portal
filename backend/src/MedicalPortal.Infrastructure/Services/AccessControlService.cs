using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Infrastructure.Services;

public record AccessDecision(bool Allowed, Guid PatientId, bool IsProxy, string? Reason = null, Guid? PatientPersonId = null, bool IsBreakGlass = false, DateTime? BreakGlassExpiresAt = null);

public interface IAccessControlService
{
    Task<AccessDecision> ResolveAccessAsync(AccessTokenClaims actor, Guid patientId, Permission permission);
    Task<AccessDecision> ResolveAccessAsync(AccessTokenClaims actor, Guid patientId, Permission permission, AppResource resource, AppAction action, Guid? departmentId = null);
    Task<bool> HasDepartmentScopeAsync(Guid userAccountId, Guid? departmentId);
    Task<List<Guid>> ListAccessiblePatientsAsync(AccessTokenClaims actor);
}

public class AccessControlService : IAccessControlService
{
    private readonly MedicalPortalDbContext _db;

    public AccessControlService(MedicalPortalDbContext db)
    {
        _db = db;
    }

    public Task<AccessDecision> ResolveAccessAsync(AccessTokenClaims actor, Guid patientId, Permission permission)
    {
        var (resource, action) = MapPermissionToResourceAction(permission);
        return ResolveAccessAsync(actor, patientId, permission, resource, action, null);
    }

    public async Task<AccessDecision> ResolveAccessAsync(
        AccessTokenClaims actor,
        Guid patientId,
        Permission permission,
        AppResource resource,
        AppAction action,
        Guid? departmentId = null)
    {
        var now = DateTime.UtcNow;
        var target = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(p => new { p.Id, p.PersonId })
            .FirstOrDefaultAsync();

        if (target == null)
        {
            return new AccessDecision(false, patientId, false, "patient_not_found");
        }

        // 1. Evaluate Permission Matrix (Role x Resource x Action)
        if (!PermissionMatrix.HasPermission(actor.Role, resource, action))
        {
            return new AccessDecision(false, patientId, false, $"role_{actor.Role.ToString().ToLowerInvariant()}_permission_denied", target.PersonId);
        }

        // 2. Department & Facility scope check
        if (departmentId.HasValue && actor.Role != UserRole.ADMIN && actor.Role != UserRole.SYSTEM_ADMIN && actor.Role != UserRole.PATIENT)
        {
            var inScope = await HasDepartmentScopeAsync(actor.UserAccountId, departmentId.Value);
            if (!inScope)
            {
                return new AccessDecision(false, patientId, false, "department_scope_mismatch", target.PersonId);
            }
        }

        // 3. Administrative roles
        if (actor.Role == UserRole.ADMIN || actor.Role == UserRole.SYSTEM_ADMIN)
        {
            return new AccessDecision(true, patientId, false, null, target.PersonId);
        }

        // 4. Compliance auditor (read-only audit inspection across records)
        if (actor.Role == UserRole.COMPLIANCE_AUDITOR)
        {
            return new AccessDecision(true, patientId, false, null, target.PersonId);
        }

        // 5. Operational staff roles (SCHEDULER, BILLING, LAB, PHARMACIST, DEPT_ADMIN, NURSE)
        if (actor.Role is UserRole.SCHEDULER or UserRole.BILLING or UserRole.LAB or UserRole.PHARMACIST or UserRole.DEPT_ADMIN or UserRole.NURSE)
        {
            var memberships = await _db.UserMemberships
                .AsNoTracking()
                .Where(m => m.UserAccountId == actor.UserAccountId)
                .ToListAsync();

            var providerDepts = await _db.Providers
                .AsNoTracking()
                .Where(pr => pr.AccountId == actor.UserAccountId)
                .SelectMany(pr => pr.ProviderDepartments)
                .Select(pd => pd.DepartmentId)
                .ToListAsync();

            // SEC-07: Unassigned staff member has no organizational grant
            if (memberships.Count == 0 && providerDepts.Count == 0)
            {
                return new AccessDecision(false, patientId, false, "unassigned_staff_scope", target.PersonId);
            }

            // Enterprise-wide grant allows access across all facilities
            var hasEnterprise = memberships.Any(m => m.FacilityId == null && m.DepartmentId == null);
            if (hasEnterprise)
            {
                return new AccessDecision(true, patientId, false, null, target.PersonId);
            }

            // Derive scope if unambiguous or reject if caller omitted required scope
            if (!departmentId.HasValue)
            {
                var distinctDepts = memberships.Where(m => m.DepartmentId.HasValue).Select(m => m.DepartmentId!.Value)
                    .Concat(providerDepts)
                    .Distinct()
                    .ToList();

                var hasFacilityWide = memberships.Any(m => m.DepartmentId == null);

                if (!hasFacilityWide)
                {
                    if (distinctDepts.Count == 1)
                    {
                        departmentId = distinctDepts[0];
                    }
                    else
                    {
                        return new AccessDecision(false, patientId, false, "department_scope_required", target.PersonId);
                    }
                }
            }

            if (departmentId.HasValue)
            {
                var inScope = await HasDepartmentScopeAsync(actor.UserAccountId, departmentId.Value);
                if (!inScope)
                {
                    return new AccessDecision(false, patientId, false, "department_scope_mismatch", target.PersonId);
                }
            }

            return new AccessDecision(true, patientId, false, null, target.PersonId);
        }

        // 6. Clinical practitioners (PHYSICIAN & CLINICIAN) - gated by care-team membership or break-glass override
        if (actor.Role == UserRole.CLINICIAN || actor.Role == UserRole.PHYSICIAN)
        {
            var provider = await _db.Providers
                .AsNoTracking()
                .Where(pr => pr.AccountId == actor.UserAccountId)
                .Select(pr => pr.Id)
                .FirstOrDefaultAsync();

            if (provider == Guid.Empty)
            {
                return new AccessDecision(false, patientId, false, "not_a_provider", target.PersonId);
            }

            var onTeam = await _db.CareTeams
                .AsNoTracking()
                .AnyAsync(ct => ct.PatientId == patientId && ct.ProviderId == provider);

            if (onTeam)
            {
                return new AccessDecision(true, patientId, false, null, target.PersonId);
            }

            // Check active break-glass emergency override
            var breakGlass = await _db.BreakGlassOverrides
                .AsNoTracking()
                .Where(b => b.ClinicianUserAccountId == actor.UserAccountId &&
                            b.PatientId == patientId &&
                            b.ExpiresAt > now &&
                            b.RevokedAt == null)
                .OrderByDescending(b => b.GrantedAt)
                .FirstOrDefaultAsync();

            if (breakGlass != null)
            {
                return new AccessDecision(true, patientId, false, "break_glass_emergency_override", target.PersonId, IsBreakGlass: true, BreakGlassExpiresAt: breakGlass.ExpiresAt);
            }

            return new AccessDecision(false, patientId, false, "not_on_care_team", target.PersonId);
        }

        // 7. Own chart
        if (target.PersonId == actor.PersonId)
        {
            return new AccessDecision(true, patientId, false, null, target.PersonId);
        }

        // 8. Proxy delegation path
        var grant = await _db.ProxyGrants
            .AsNoTracking()
            .Where(g => g.GranteeId == actor.PersonId &&
                        g.GrantorId == target.PersonId &&
                        g.RevokedAt == null &&
                        (g.ConsentExpiresAt == null || g.ConsentExpiresAt > now))
            .FirstOrDefaultAsync();

        if (grant == null)
        {
            return new AccessDecision(false, patientId, false, "no_active_grant", target.PersonId);
        }

        var permitted = permission switch
        {
            Permission.viewRecords => grant.CanViewRecords,
            Permission.message => grant.CanMessage,
            Permission.manageAppointments => grant.CanManageAppointments,
            Permission.requestRefills => grant.CanRequestRefills,
            Permission.payBills => grant.CanPayBills,
            Permission.manageSharing => grant.CanManageSharing,
            Permission.submitForms => grant.CanSubmitForms,
            _ => false
        };

        if (!permitted)
        {
            return new AccessDecision(false, patientId, true, "permission_not_granted", target.PersonId);
        }

        return new AccessDecision(true, patientId, true, null, target.PersonId);
    }

    public async Task<bool> HasDepartmentScopeAsync(Guid userAccountId, Guid? departmentId)
    {
        if (!departmentId.HasValue) return true;

        var memberships = await _db.UserMemberships
            .AsNoTracking()
            .Where(m => m.UserAccountId == userAccountId)
            .ToListAsync();

        if (memberships.Count == 0)
        {
            // If user is a provider, check ProviderDepartments
            var providerDepts = await _db.Providers
                .AsNoTracking()
                .Where(pr => pr.AccountId == userAccountId)
                .SelectMany(pr => pr.ProviderDepartments)
                .Select(pd => pd.DepartmentId)
                .ToListAsync();

            if (providerDepts.Count > 0)
            {
                return providerDepts.Contains(departmentId.Value);
            }

            // Unassigned staff member has no department scope
            return false;
        }

        // Direct department membership
        if (memberships.Any(m => m.DepartmentId == departmentId.Value))
        {
            return true;
        }

        // Enterprise-wide membership
        if (memberships.Any(m => m.FacilityId == null && m.DepartmentId == null))
        {
            return true;
        }

        // Facility-level membership containing this department
        var targetDeptFacilityId = await _db.Departments
            .AsNoTracking()
            .Where(d => d.Id == departmentId.Value)
            .Select(d => d.FacilityId)
            .FirstOrDefaultAsync();

        if (targetDeptFacilityId != Guid.Empty && memberships.Any(m => m.FacilityId == targetDeptFacilityId && m.DepartmentId == null))
        {
            return true;
        }

        return false;
    }

    public async Task<List<Guid>> ListAccessiblePatientsAsync(AccessTokenClaims actor)
    {
        if (actor.Role == UserRole.ADMIN || actor.Role == UserRole.SYSTEM_ADMIN || actor.Role == UserRole.COMPLIANCE_AUDITOR)
        {
            return await _db.Patients.AsNoTracking().Select(p => p.Id).ToListAsync();
        }

        if (actor.Role == UserRole.CLINICIAN || actor.Role == UserRole.PHYSICIAN)
        {
            var providerId = await _db.Providers
                .AsNoTracking()
                .Where(pr => pr.AccountId == actor.UserAccountId)
                .Select(pr => pr.Id)
                .FirstOrDefaultAsync();

            if (providerId == Guid.Empty)
                return new List<Guid>();

            var careTeamPatients = await _db.CareTeams
                .AsNoTracking()
                .Where(ct => ct.ProviderId == providerId)
                .OrderByDescending(ct => ct.IsPrimary)
                .Select(ct => ct.PatientId)
                .ToListAsync();

            var breakGlassPatients = await _db.BreakGlassOverrides
                .AsNoTracking()
                .Where(b => b.ClinicianUserAccountId == actor.UserAccountId &&
                            b.ExpiresAt > DateTime.UtcNow &&
                            b.RevokedAt == null)
                .Select(b => b.PatientId)
                .ToListAsync();

            return careTeamPatients.Concat(breakGlassPatients).Distinct().ToList();
        }

        // Patient role: own patient ID
        var ownPatientIds = await _db.Patients
            .AsNoTracking()
            .Where(p => p.PersonId == actor.PersonId)
            .Select(p => p.Id)
            .ToListAsync();

        // Proxy patients
        var now = DateTime.UtcNow;
        var proxyPatientIds = await _db.ProxyGrants
            .AsNoTracking()
            .Where(g => g.GranteeId == actor.PersonId &&
                        g.RevokedAt == null &&
                        (g.ConsentExpiresAt == null || g.ConsentExpiresAt > now) &&
                        g.CanViewRecords)
            .Join(_db.Patients, g => g.GrantorId, p => p.PersonId, (g, p) => p.Id)
            .ToListAsync();

        return ownPatientIds.Concat(proxyPatientIds).Distinct().ToList();
    }

    private static (AppResource Resource, AppAction Action) MapPermissionToResourceAction(Permission permission)
    {
        return permission switch
        {
            Permission.viewRecords => (AppResource.ClinicalNotes, AppAction.Read),
            Permission.manageAppointments => (AppResource.Appointments, AppAction.Write),
            Permission.requestRefills => (AppResource.Medications, AppAction.Write),
            Permission.payBills => (AppResource.Billing, AppAction.Write),
            Permission.message => (AppResource.Messages, AppAction.Write),
            Permission.manageSharing => (AppResource.ClinicalNotes, AppAction.Write),
            _ => (AppResource.ClinicalNotes, AppAction.Read)
        };
    }
}
