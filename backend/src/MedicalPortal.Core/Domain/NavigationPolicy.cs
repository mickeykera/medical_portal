using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Domain;

public static class NavigationPolicy
{
    private static readonly IReadOnlyDictionary<UserRole, IReadOnlyList<string>> Modules =
        new Dictionary<UserRole, IReadOnlyList<string>>
        {
            [UserRole.PATIENT] = ["overview", "trends", "forms", "messages", "billing", "family", "sharing", "settings"],
            [UserRole.CLINICIAN] = ["cockpit", "overview", "messages", "settings"],
            [UserRole.PHYSICIAN] = ["cockpit", "overview", "messages", "settings"],
            [UserRole.NURSE] = ["cockpit", "overview", "settings"],
            [UserRole.SCHEDULER] = ["overview", "settings"],
            [UserRole.BILLING] = ["billing", "settings"],
            [UserRole.LAB] = ["overview", "settings"],
            [UserRole.PHARMACIST] = ["overview", "settings"],
            [UserRole.DEPT_ADMIN] = ["overview", "settings"],
            [UserRole.COMPLIANCE_AUDITOR] = ["overview", "settings"],
            [UserRole.ADMIN] = ["admin", "system-status", "settings"],
            [UserRole.SYSTEM_ADMIN] = ["admin", "system-status", "settings"],
            [UserRole.PLATFORM_ADMIN] = ["admin", "system-status", "settings"]
        };

    public static IReadOnlyList<string> ForRole(UserRole role) =>
        Modules.TryGetValue(role, out var modules) ? modules : Array.Empty<string>();
}
