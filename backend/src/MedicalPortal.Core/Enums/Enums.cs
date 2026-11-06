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
    SYSTEM_ADMIN,
    PLATFORM_ADMIN
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
    REHAB,
    UNIT,
    ROOM
}

public enum PatientClass
{
    OUTPATIENT,
    INPATIENT,
    EMERGENCY,
    DAY_CASE,
    OBSERVATION
}

public enum EncounterStatus
{
    PLANNED,
    ACTIVE,
    COMPLETED,
    CANCELLED
}

public enum BedState
{
    AVAILABLE,
    OCCUPIED,
    RESERVED,
    CLEANING,
    BLOCKED,
    OUT_OF_SERVICE
}

public enum ClinicalOrderType
{
    LAB,
    IMAGING,
    PHYSIOLOGY,
    PATHOLOGY,
    MEDICATION,
    PROCEDURE,
    NURSING_TASK,
    DIET,
    CONSULT,
    HOUSEKEEPING
}

public enum OrderPriority
{
    ROUTINE,
    URGENT,
    STAT
}

public enum ClinicalOrderStatus
{
    DRAFT,
    PENDING_PAYMENT,
    RELEASED,
    IN_PROGRESS,
    RESULTED,
    VERIFIED,
    DELIVERED,
    CLOSED,
    CANCELLED,
    REJECTED
}

public enum PaymentGateState
{
    NOT_REQUIRED,
    AWAITING_PAYMENT,
    PAID,
    WAIVED,
    BILL_LATER
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
