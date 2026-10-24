using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Fhir;

public class ClinicalCoreFhirClient : IClinicalCoreFhirClient
{
    private readonly HttpClient _httpClient;
    private readonly ClinicalCoreFhirOptions _options;
    private readonly ILogger<ClinicalCoreFhirClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ClinicalCoreFhirClient(
        HttpClient httpClient,
        IOptions<ClinicalCoreFhirOptions> options,
        ILogger<ClinicalCoreFhirClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            var baseUri = _options.BaseUrl.EndsWith("/") ? _options.BaseUrl : _options.BaseUrl + "/";
            _httpClient.BaseAddress = new Uri(baseUri);
        }

        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(_options.Username) && !string.IsNullOrEmpty(_options.Password))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        if (_options.TimeoutSeconds > 0)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        }
    }

    public async Task<FhirCapabilityStatementDto?> GetCapabilityStatementAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("metadata", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Clinical core returned status {StatusCode} for metadata query.", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<FhirCapabilityStatementDto>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve FHIR metadata from clinical core.");
            return null;
        }
    }

    public async Task<FhirPatientDto?> GetPatientByIdAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"Patient/{Uri.EscapeDataString(fhirPatientId)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<FhirPatientDto>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch patient {PatientId} from clinical core.", fhirPatientId);
            return null;
        }
    }

    public async Task<FhirPatientDto?> GetPatientByMrnAsync(string mrn, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"Patient?identifier={Uri.EscapeDataString(mrn)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var bundle = JsonSerializer.Deserialize<FhirBundleDto<FhirPatientDto>>(content, JsonOptions);
            return bundle?.Resources.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search patient by MRN from clinical core.");
            return null;
        }
    }

    public async Task<FhirBundleDto<FhirEncounterDto>> GetEncountersAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        return await GetBundleAsync<FhirEncounterDto>($"Encounter?subject=Patient/{Uri.EscapeDataString(fhirPatientId)}", cancellationToken);
    }

    public async Task<FhirBundleDto<FhirObservationDto>> GetObservationsAsync(string fhirPatientId, string? category = null, CancellationToken cancellationToken = default)
    {
        var url = $"Observation?subject=Patient/{Uri.EscapeDataString(fhirPatientId)}";
        if (!string.IsNullOrEmpty(category))
        {
            url += $"&category={Uri.EscapeDataString(category)}";
        }
        return await GetBundleAsync<FhirObservationDto>(url, cancellationToken);
    }

    public async Task<FhirBundleDto<FhirAllergyIntoleranceDto>> GetAllergiesAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        return await GetBundleAsync<FhirAllergyIntoleranceDto>($"AllergyIntolerance?patient=Patient/{Uri.EscapeDataString(fhirPatientId)}", cancellationToken);
    }

    public async Task<FhirBundleDto<FhirMedicationRequestDto>> GetMedicationsAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        return await GetBundleAsync<FhirMedicationRequestDto>($"MedicationRequest?subject=Patient/{Uri.EscapeDataString(fhirPatientId)}", cancellationToken);
    }

    public async Task<FhirBundleDto<FhirAppointmentDto>> GetAppointmentsAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        return await GetBundleAsync<FhirAppointmentDto>($"Appointment?patient=Patient/{Uri.EscapeDataString(fhirPatientId)}", cancellationToken);
    }

    public async Task<FhirAppointmentDto?> CreateAppointmentAsync(FhirAppointmentDto appointment, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(appointment, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/fhir+json");
            using var response = await _httpClient.PostAsync("Appointment", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<FhirAppointmentDto>(responseJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create appointment in clinical core.");
            return null;
        }
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var capability = await GetCapabilityStatementAsync(cancellationToken);
            return capability != null && capability.Status == "active";
        }
        catch
        {
            return false;
        }
    }

    private async Task<FhirBundleDto<T>> GetBundleAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new FhirBundleDto<T>("Bundle", "searchset", 0, new List<FhirBundleEntryDto<T>>());
            }
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var bundle = JsonSerializer.Deserialize<FhirBundleDto<T>>(content, JsonOptions);
            return bundle ?? new FhirBundleDto<T>("Bundle", "searchset", 0, new List<FhirBundleEntryDto<T>>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bundle from {Url}", relativeUrl);
            return new FhirBundleDto<T>("Bundle", "searchset", 0, new List<FhirBundleEntryDto<T>>());
        }
    }
}
