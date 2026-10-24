namespace MedicalPortal.Core.Enums;

/// <summary>
/// Workflow lifecycle for DHIS2 HMIS aggregate reporting datasets.
/// </summary>
public enum Dhis2ReportStatus
{
    Draft,
    DryRun,
    FacilityApproved,
    Submitted,
    Imported,
    Rejected
}

/// <summary>
/// Direction of an eCHIS community referral.
/// </summary>
public enum EchisReferralDirection
{
    Inbound,   // Community Health Extension Worker (HEW) -> Hospital
    Outbound   // Hospital -> Community Health Extension Worker (HEW)
}

/// <summary>
/// Clinical and triage status of an eCHIS referral.
/// </summary>
public enum EchisReferralStatus
{
    PendingTriage,
    Accepted,
    Completed,
    Rejected,
    Cancelled
}

/// <summary>
/// Status of an identified potential duplicate patient record.
/// </summary>
public enum DuplicateResolutionStatus
{
    Pending,
    Merged,
    DismissedNotDuplicate
}
