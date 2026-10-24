namespace MedicalPortal.Infrastructure.Fhir;

public class ClinicalCoreFhirOptions
{
    public const string SectionName = "ClinicalCore";

    /// <summary>
    /// Base URL to the FHIR R4 endpoint (e.g. http://openmrs:8080/openmrs/ws/fhir2/R4/ or https://core.hospital.internal/openmrs/ws/fhir2/R4/).
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8080/openmrs/ws/fhir2/R4/";

    /// <summary>
    /// Base URL to the OpenMRS O3 Microfrontend Single Page Application for clinician charting deep-links.
    /// (e.g. https://emr.hospital.internal/openmrs/spa)
    /// </summary>
    public string O3BaseUrl { get; set; } = "https://emr.hospital.internal/openmrs/spa";

    /// <summary>
    /// Service account username for OpenMRS REST/FHIR authentication.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Service account password for OpenMRS REST/FHIR authentication.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// HTTP request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, activates FakeClinicalCoreFhirClient for unit tests and local mock development.
    /// In production, fail-fast validations ensure this is false.
    /// </summary>
    public bool UseFake { get; set; } = false;
}
