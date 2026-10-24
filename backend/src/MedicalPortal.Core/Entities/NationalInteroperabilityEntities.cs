using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

/// <summary>
/// National DHIS2 HMIS aggregate reporting dataset.
/// Captures indicators, dry-run validations, facility approvals, submission logs,
/// and offline signed JSON export fallback.
/// </summary>
public class Dhis2ReportDataset : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;

    /// <summary>
    /// DHIS2 DataSet UID, e.g. "HMIS-MOH-MONTHLY".
    /// </summary>
    public string DataSetId { get; set; } = "HMIS-MOH-MONTHLY";

    /// <summary>
    /// Reporting period, e.g. "201902" (Ethiopian monthly) or "2019Q1" (Ethiopian fiscal quarter).
    /// </summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>
    /// DHIS2 OrgUnit UID identifying the reporting hospital facility.
    /// </summary>
    public string OrgUnitId { get; set; } = string.Empty;

    /// <summary>
    /// Current submission workflow status.
    /// </summary>
    public Dhis2ReportStatus Status { get; set; } = Dhis2ReportStatus.Draft;

    /// <summary>
    /// Serialized JSON array of { dataElement, categoryOptionCombo, value }.
    /// </summary>
    public string DataValuesJson { get; set; } = "[]";

    /// <summary>
    /// Import summary response from dry-run validation.
    /// </summary>
    public string? DryRunSummaryJson { get; set; }

    /// <summary>
    /// Authoritative import summary returned by DHIS2 server upon successful submission.
    /// </summary>
    public string? ImportSummaryJson { get; set; }

    /// <summary>
    /// Username or clinician identity who authorized facility approval.
    /// </summary>
    public string? ApprovedBy { get; set; }

    /// <summary>
    /// UTC timestamp of facility leadership approval.
    /// </summary>
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when report was transmitted to DHIS2.
    /// </summary>
    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>
    /// Cryptographically signed JSON export bundle used for offline woreda submission.
    /// </summary>
    public string? ExportPayloadJson { get; set; }

    /// <summary>
    /// HMAC signature of the export payload.
    /// </summary>
    public string? ExportSignature { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// National Electronic Community Health Information System (eCHIS) referral.
/// Enables bi-directional referrals between health extension workers and hospitals.
/// </summary>
public class EchisReferral : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;

    /// <summary>
    /// Unique national eCHIS referral tracking identifier, e.g. "ECHIS-REF-2026-0001".
    /// </summary>
    public string ReferralNumber { get; set; } = string.Empty;

    /// <summary>
    /// Inbound (HEW -> Hospital) or Outbound (Hospital -> HEW).
    /// </summary>
    public EchisReferralDirection Direction { get; set; } = EchisReferralDirection.Inbound;

    /// <summary>
    /// Local hospital patient ID if linked or triaged.
    /// </summary>
    public Guid? PatientId { get; set; }

    /// <summary>
    /// National Digital ID / Fayya identifier.
    /// </summary>
    public string? NationalPatientId { get; set; }

    public string PatientFullName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;

    /// <summary>
    /// Referring Health Post / Health Center or Hospital Unit.
    /// </summary>
    public string ReferringUnit { get; set; } = string.Empty;

    /// <summary>
    /// Receiving health facility / hospital department.
    /// </summary>
    public string ReceivingUnit { get; set; } = string.Empty;

    public string ReasonForReferral { get; set; } = string.Empty;
    public string Urgency { get; set; } = "Routine"; // Routine, Urgent, Emergency

    public EchisReferralStatus Status { get; set; } = EchisReferralStatus.PendingTriage;

    /// <summary>
    /// Clinical summary formatted as FHIR R4 ServiceRequest or IPS JSON.
    /// </summary>
    public string ClinicalSummaryJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? TriagedAtUtc { get; set; }
    public string? TriageNotes { get; set; }

    public virtual Patient? Patient { get; set; }
}

/// <summary>
/// Candidate duplicate patient identified by demographic matching rules.
/// Supports the national Master Patient Index (MPI) deduplication workflow.
/// </summary>
public class PatientDuplicateCandidate : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;

    public Guid PrimaryPatientId { get; set; }
    public Guid CandidateDuplicatePatientId { get; set; }

    /// <summary>
    /// Match confidence score from 0.00 to 1.00.
    /// </summary>
    public decimal ConfidenceScore { get; set; }

    /// <summary>
    /// JSON array of matching rules triggered, e.g. ["Exact Phone Match", "Matching Date of Birth", "Soundex Name Match"].
    /// </summary>
    public string MatchReasonsJson { get; set; } = "[]";

    public DuplicateResolutionStatus Status { get; set; } = DuplicateResolutionStatus.Pending;

    public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNotes { get; set; }

    public virtual Patient PrimaryPatient { get; set; } = null!;
    public virtual Patient CandidateDuplicatePatient { get; set; } = null!;
    public virtual UserAccount? ResolvedByUser { get; set; }
}
