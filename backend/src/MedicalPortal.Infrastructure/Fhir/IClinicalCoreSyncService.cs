namespace MedicalPortal.Infrastructure.Fhir;

/// <summary>
/// Clinical core synchronization service interface.
/// Orchestrates bidirectional data synchronization between the OpenMRS 2.8+ / O3 Clinical Core
/// and the portal database read-through cache using FHIR R4 JSON REST endpoints.
/// </summary>
public interface IClinicalCoreSyncService
{
    /// <summary>
    /// Synchronizes patient clinical data (Observations, Allergies, Medications, Encounters)
    /// from the Clinical Core (OpenMRS FHIR R4) into the portal read-through cache / snapshot.
    /// If the core is offline, catches exceptions and falls back cleanly to local DB without throwing.
    /// Returns true if synchronization from core succeeded; false if core was unreachable.
    /// </summary>
    Task<bool> SyncPatientClinicalDataAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes outbound appointments booked on the portal into OpenMRS FHIR R4.
    /// </summary>
    Task<int> SyncOutboundAppointmentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inbound background pull for stale patients.
    /// </summary>
    Task<int> SyncStalePatientsAsync(int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the deep-link URL for clinician encounter charting in the OpenMRS O3 UI.
    /// e.g. https://emr.hospital.internal/openmrs/spa/patient/{fhirPatientId}/chart
    /// </summary>
    Task<string?> GetO3PatientChartUrlAsync(Guid patientId, CancellationToken cancellationToken = default);
}
