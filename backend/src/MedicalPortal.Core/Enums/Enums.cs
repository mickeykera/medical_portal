namespace MedicalPortal.Core.Enums;

public enum UserRole
{
    PATIENT,
    CLINICIAN,
    ADMIN,
    SCHEDULER,
    NURSE,
    PHYSICIAN,
    BILLING,
    LAB,
    PHARMACIST,
    DEPT_ADMIN,
    COMPLIANCE_AUDITOR,
    SYSTEM_ADMIN
}

public enum AppointmentStatus
{
    REQUESTED,
    CONFIRMED,
    COMPLETED,
    CANCELLED_BY_PATIENT,
    CANCELLED_BY_CLINIC,
    NO_SHOW
}

public enum AllergySeverity
{
    MILD,
    MODERATE,
    SEVERE,
    ANAPHYLAXIS
}

public enum ObservationCategory
{
    VITAL,
    LAB,
    IMMUNIZATION,
    PROCEDURE,
    SOCIAL_HISTORY
}

public enum RefillStatus
{
    PENDING,
    APPROVED,
    DENIED
}

public enum StatementStatus
{
    OPEN,
    PARTIALLY_PAID,
    PAID,
    VOID
}

public enum PaymentStatus
{
    SUCCEEDED,
    FAILED,
    PENDING,
    REFUNDED
}

public enum PaymentPlanStatus
{
    ACTIVE,
    COMPLETED,
    CANCELLED
}

public enum ShareStatus
{
    ACTIVE,
    REVOKED,
    EXPIRED,
    EXHAUSTED
}

public enum LocationType
{
    CLINIC,
    HOSPITAL,
    URGENT_CARE,
    PHARMACY,
    LAB,
    REHAB
}

public enum Permission
{
    viewRecords,
    message,
    manageAppointments,
    requestRefills,
    payBills,
    manageSharing,
    submitForms
}
