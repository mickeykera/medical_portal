namespace MedicalPortal.Core.Enums;

/// <summary>
/// Operational role of the deployed application instance.
/// In Facility mode, the node acts as the authoritative LAN system of record.
/// In RegionalCloud mode, the node acts as an aggregator and replica.
/// </summary>
public enum DeploymentNodeRole
{
    Facility,
    RegionalCloud
}

/// <summary>
/// Processing lifecycle state of a transactional outbox message.
/// </summary>
public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Delivered,
    FailedTransient,
    DeadLetter
}

/// <summary>
/// Status of a signed reference data package.
/// </summary>
public enum ReferencePackageStatus
{
    Draft,
    Installed,
    Deprecated,
    Rejected
}
