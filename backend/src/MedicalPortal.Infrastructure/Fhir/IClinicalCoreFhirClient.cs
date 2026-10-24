namespace MedicalPortal.Infrastructure.Fhir;

/// <summary>
/// FHIR R4 client interface for bidirectional communication with the clinical core (OpenMRS 2.8+ / O3).
/// The portal accesses clinical charts, encounters, vitals, lab results, allergies, medications,
/// and appointments strictly through this interface over standard FHIR R4 JSON REST endpoints.
/// </summary>
public interface IClinicalCoreFhirClient
{
    /// <summary>
    /// Fetches the FHIR CapabilityStatement (metadata) from the core.
    /// </summary>
    Task<FhirCapabilityStatementDto?> GetCapabilityStatementAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a patient resource by its FHIR logical ID (UUID).
    /// </summary>
    Task<FhirPatientDto?> GetPatientByIdAsync(string fhirPatientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for a patient by medical record number (MRN identifier).
    /// </summary>
    Task<FhirPatientDto?> GetPatientByMrnAsync(string mrn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches encounters for a given patient.
    /// </summary>
    Task<FhirBundleDto<FhirEncounterDto>> GetEncountersAsync(string fhirPatientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches clinical observations (vitals, lab results) for a given patient.
    /// </summary>
    Task<FhirBundleDto<FhirObservationDto>> GetObservationsAsync(string fhirPatientId, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches recorded allergy intolerances for a given patient.
    /// </summary>
    Task<FhirBundleDto<FhirAllergyIntoleranceDto>> GetAllergiesAsync(string fhirPatientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches active and past medication requests (prescriptions) for a given patient.
    /// </summary>
    Task<FhirBundleDto<FhirMedicationRequestDto>> GetMedicationsAsync(string fhirPatientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches appointments scheduled in the clinical core for a given patient.
    /// </summary>
    Task<FhirBundleDto<FhirAppointmentDto>> GetAppointmentsAsync(string fhirPatientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Books or requests an appointment in the clinical core.
    /// </summary>
    Task<FhirAppointmentDto?> CreateAppointmentAsync(FhirAppointmentDto appointment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks connectivity and FHIR capability readiness of the clinical core.
    /// </summary>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
