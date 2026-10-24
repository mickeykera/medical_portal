namespace MedicalPortal.Core.Enums;

public enum HmsDeadLetterStatus
{
    Pending,
    Retrying,
    Resolved,
    Discarded
}

public enum HmsConflictStatus
{
    Pending,
    ResolvedKeepLocal,
    ResolvedAcceptHms,
    Ignored
}
