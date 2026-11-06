using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Domain;

public enum AppResource
{
    Appointments,
    ClinicalNotes,
    ClinicalResults,
    Medications,
    Billing,
    Messages,
    AuditCompliance,
    Orders
}

public enum AppAction
{
    Read,
    Write,
    Delete,
    Admin
}

public static class PermissionMatrix
{
    private static readonly HashSet<(UserRole Role, AppResource Resource, AppAction Action)> AllowedPermissions = new()
    {
        // SYSTEM_ADMIN & ADMIN: Full unrestricted access
        (UserRole.SYSTEM_ADMIN, AppResource.Appointments, AppAction.Read),
        (UserRole.SYSTEM_ADMIN, AppResource.Appointments, AppAction.Write),
        (UserRole.SYSTEM_ADMIN, AppResource.Appointments, AppAction.Delete),
        (UserRole.SYSTEM_ADMIN, AppResource.Appointments, AppAction.Admin),
        (UserRole.SYSTEM_ADMIN, AppResource.ClinicalNotes, AppAction.Read),
        (UserRole.SYSTEM_ADMIN, AppResource.ClinicalNotes, AppAction.Write),
        (UserRole.SYSTEM_ADMIN, AppResource.ClinicalNotes, AppAction.Admin),
        (UserRole.SYSTEM_ADMIN, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.SYSTEM_ADMIN, AppResource.ClinicalResults, AppAction.Write),
        (UserRole.SYSTEM_ADMIN, AppResource.ClinicalResults, AppAction.Admin),
        (UserRole.SYSTEM_ADMIN, AppResource.Medications, AppAction.Read),
        (UserRole.SYSTEM_ADMIN, AppResource.Medications, AppAction.Write),
        (UserRole.SYSTEM_ADMIN, AppResource.Medications, AppAction.Admin),
        (UserRole.SYSTEM_ADMIN, AppResource.Billing, AppAction.Read),
        (UserRole.SYSTEM_ADMIN, AppResource.Billing, AppAction.Write),
        (UserRole.SYSTEM_ADMIN, AppResource.Billing, AppAction.Admin),
        (UserRole.SYSTEM_ADMIN, AppResource.Messages, AppAction.Read),
        (UserRole.SYSTEM_ADMIN, AppResource.Messages, AppAction.Write),
        (UserRole.SYSTEM_ADMIN, AppResource.Messages, AppAction.Admin),
        (UserRole.SYSTEM_ADMIN, AppResource.AuditCompliance, AppAction.Read),
        (UserRole.SYSTEM_ADMIN, AppResource.AuditCompliance, AppAction.Admin),
        (UserRole.SYSTEM_ADMIN, AppResource.Orders, AppAction.Read),
        (UserRole.SYSTEM_ADMIN, AppResource.Orders, AppAction.Write),
        (UserRole.SYSTEM_ADMIN, AppResource.Orders, AppAction.Admin),

        (UserRole.ADMIN, AppResource.Appointments, AppAction.Read),
        (UserRole.ADMIN, AppResource.Appointments, AppAction.Write),
        (UserRole.ADMIN, AppResource.Appointments, AppAction.Delete),
        (UserRole.ADMIN, AppResource.Appointments, AppAction.Admin),
        (UserRole.ADMIN, AppResource.ClinicalNotes, AppAction.Read),
        (UserRole.ADMIN, AppResource.ClinicalNotes, AppAction.Write),
        (UserRole.ADMIN, AppResource.ClinicalNotes, AppAction.Admin),
        (UserRole.ADMIN, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.ADMIN, AppResource.ClinicalResults, AppAction.Write),
        (UserRole.ADMIN, AppResource.ClinicalResults, AppAction.Admin),
        (UserRole.ADMIN, AppResource.Medications, AppAction.Read),
        (UserRole.ADMIN, AppResource.Medications, AppAction.Write),
        (UserRole.ADMIN, AppResource.Medications, AppAction.Admin),
        (UserRole.ADMIN, AppResource.Billing, AppAction.Read),
        (UserRole.ADMIN, AppResource.Billing, AppAction.Write),
        (UserRole.ADMIN, AppResource.Billing, AppAction.Admin),
        (UserRole.ADMIN, AppResource.Messages, AppAction.Read),
        (UserRole.ADMIN, AppResource.Messages, AppAction.Write),
        (UserRole.ADMIN, AppResource.Messages, AppAction.Admin),
        (UserRole.ADMIN, AppResource.AuditCompliance, AppAction.Read),
        (UserRole.ADMIN, AppResource.AuditCompliance, AppAction.Admin),
        (UserRole.ADMIN, AppResource.Orders, AppAction.Read),
        (UserRole.ADMIN, AppResource.Orders, AppAction.Write),
        (UserRole.ADMIN, AppResource.Orders, AppAction.Admin),

        // PHYSICIAN & CLINICIAN: Full clinical care rights
        (UserRole.PHYSICIAN, AppResource.Appointments, AppAction.Read),
        (UserRole.PHYSICIAN, AppResource.Appointments, AppAction.Write),
        (UserRole.PHYSICIAN, AppResource.ClinicalNotes, AppAction.Read),
        (UserRole.PHYSICIAN, AppResource.ClinicalNotes, AppAction.Write),
        (UserRole.PHYSICIAN, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.PHYSICIAN, AppResource.ClinicalResults, AppAction.Write),
        (UserRole.PHYSICIAN, AppResource.Medications, AppAction.Read),
        (UserRole.PHYSICIAN, AppResource.Medications, AppAction.Write),
        (UserRole.PHYSICIAN, AppResource.Billing, AppAction.Read), // viewing snapshot balance
        (UserRole.PHYSICIAN, AppResource.Messages, AppAction.Read),
        (UserRole.PHYSICIAN, AppResource.Messages, AppAction.Write),
        (UserRole.PHYSICIAN, AppResource.Orders, AppAction.Read),
        (UserRole.PHYSICIAN, AppResource.Orders, AppAction.Write),

        (UserRole.CLINICIAN, AppResource.Appointments, AppAction.Read),
        (UserRole.CLINICIAN, AppResource.Appointments, AppAction.Write),
        (UserRole.CLINICIAN, AppResource.ClinicalNotes, AppAction.Read),
        (UserRole.CLINICIAN, AppResource.ClinicalNotes, AppAction.Write),
        (UserRole.CLINICIAN, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.CLINICIAN, AppResource.ClinicalResults, AppAction.Write),
        (UserRole.CLINICIAN, AppResource.Medications, AppAction.Read),
        (UserRole.CLINICIAN, AppResource.Medications, AppAction.Write),
        (UserRole.CLINICIAN, AppResource.Billing, AppAction.Read),
        (UserRole.CLINICIAN, AppResource.Messages, AppAction.Read),
        (UserRole.CLINICIAN, AppResource.Messages, AppAction.Write),
        (UserRole.CLINICIAN, AppResource.Orders, AppAction.Read),
        (UserRole.CLINICIAN, AppResource.Orders, AppAction.Write),

        // NURSE: Care delivery (cannot prescribe or manage billing)
        (UserRole.NURSE, AppResource.Appointments, AppAction.Read),
        (UserRole.NURSE, AppResource.Appointments, AppAction.Write),
        (UserRole.NURSE, AppResource.ClinicalNotes, AppAction.Read),
        (UserRole.NURSE, AppResource.ClinicalNotes, AppAction.Write),
        (UserRole.NURSE, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.NURSE, AppResource.ClinicalResults, AppAction.Write),
        (UserRole.NURSE, AppResource.Medications, AppAction.Read),
        (UserRole.NURSE, AppResource.Messages, AppAction.Read),
        (UserRole.NURSE, AppResource.Messages, AppAction.Write),
        (UserRole.NURSE, AppResource.Orders, AppAction.Read),
        (UserRole.NURSE, AppResource.Orders, AppAction.Write),

        // SCHEDULER: Appointments only. MUST NOT READ RESULTS.
        (UserRole.SCHEDULER, AppResource.Appointments, AppAction.Read),
        (UserRole.SCHEDULER, AppResource.Appointments, AppAction.Write),
        (UserRole.SCHEDULER, AppResource.Appointments, AppAction.Delete),

        // BILLING: Financial workflows and appointment encounter verification. MUST NOT READ CLINICAL NOTES.
        (UserRole.BILLING, AppResource.Billing, AppAction.Read),
        (UserRole.BILLING, AppResource.Billing, AppAction.Write),
        (UserRole.BILLING, AppResource.Appointments, AppAction.Read),
        (UserRole.BILLING, AppResource.Orders, AppAction.Read),
        (UserRole.BILLING, AppResource.Orders, AppAction.Admin),

        // LAB: Results diagnostic workflows only
        (UserRole.LAB, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.LAB, AppResource.ClinicalResults, AppAction.Write),
        (UserRole.LAB, AppResource.Orders, AppAction.Read),
        (UserRole.LAB, AppResource.Orders, AppAction.Write),

        // PHARMACIST: Medication management and pertinent lab results
        (UserRole.PHARMACIST, AppResource.Medications, AppAction.Read),
        (UserRole.PHARMACIST, AppResource.Medications, AppAction.Write),
        (UserRole.PHARMACIST, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.PHARMACIST, AppResource.Messages, AppAction.Read),
        (UserRole.PHARMACIST, AppResource.Messages, AppAction.Write),
        (UserRole.PHARMACIST, AppResource.Orders, AppAction.Read),
        (UserRole.PHARMACIST, AppResource.Orders, AppAction.Write),

        // DEPT_ADMIN: Departmental administrative operations
        (UserRole.DEPT_ADMIN, AppResource.Appointments, AppAction.Read),
        (UserRole.DEPT_ADMIN, AppResource.Appointments, AppAction.Write),
        (UserRole.DEPT_ADMIN, AppResource.Billing, AppAction.Read),
        (UserRole.DEPT_ADMIN, AppResource.AuditCompliance, AppAction.Read),
        (UserRole.DEPT_ADMIN, AppResource.Messages, AppAction.Read),

        // COMPLIANCE_AUDITOR: Read-only audit inspection across records
        (UserRole.COMPLIANCE_AUDITOR, AppResource.AuditCompliance, AppAction.Read),
        (UserRole.COMPLIANCE_AUDITOR, AppResource.AuditCompliance, AppAction.Admin),
        (UserRole.COMPLIANCE_AUDITOR, AppResource.ClinicalNotes, AppAction.Read),
        (UserRole.COMPLIANCE_AUDITOR, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.COMPLIANCE_AUDITOR, AppResource.Appointments, AppAction.Read),
        (UserRole.COMPLIANCE_AUDITOR, AppResource.Billing, AppAction.Read),
        (UserRole.COMPLIANCE_AUDITOR, AppResource.Medications, AppAction.Read),

        // PLATFORM_ADMIN: Multi-tenant and infrastructure management ONLY. NO ACCESS TO PHI.
        (UserRole.PLATFORM_ADMIN, AppResource.AuditCompliance, AppAction.Read),
        (UserRole.PLATFORM_ADMIN, AppResource.AuditCompliance, AppAction.Admin),

        // PATIENT: Patient chart access
        (UserRole.PATIENT, AppResource.Appointments, AppAction.Read),
        (UserRole.PATIENT, AppResource.Appointments, AppAction.Write),
        (UserRole.PATIENT, AppResource.ClinicalNotes, AppAction.Read),
        (UserRole.PATIENT, AppResource.ClinicalResults, AppAction.Read),
        (UserRole.PATIENT, AppResource.ClinicalResults, AppAction.Write),
        (UserRole.PATIENT, AppResource.Medications, AppAction.Read),
        (UserRole.PATIENT, AppResource.Medications, AppAction.Write),
        (UserRole.PATIENT, AppResource.Billing, AppAction.Read),
        (UserRole.PATIENT, AppResource.Billing, AppAction.Write),
        (UserRole.PATIENT, AppResource.Messages, AppAction.Read),
        (UserRole.PATIENT, AppResource.Messages, AppAction.Write)
    };

    public static bool HasPermission(UserRole role, AppResource resource, AppAction action)
    {
        return AllowedPermissions.Contains((role, resource, action));
    }
}
