using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Hms;

public class HmsClient : IHmsClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<HmsClient> _logger;

    private readonly SemaphoreSlim _authLock = new(1, 1);
    private string? _cachedToken;
    private DateTime? _tokenExpiry;
    private int _consecutiveAuthFailures = 0;
    private DateTime? _authLockoutUntil;

    public HmsClient(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<HmsClient>? logger = null)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger ?? NullLogger<HmsClient>.Instance;

        var baseUrl = _config["Hms:BaseUrl"] ?? _config["HMS_BASE_URL"] ?? "http://localhost:3210";
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }
    }

    public async Task<string?> LoginAsync(CancellationToken cancellationToken = default)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            // Check lockout prevention
            if (_authLockoutUntil.HasValue && DateTime.UtcNow < _authLockoutUntil.Value)
            {
                _logger.LogWarning("HMS client authentication is temporarily locked out until {LockoutUntil} UTC due to repeated credential failures.", _authLockoutUntil.Value);
                return null;
            }

            // Return cached token if still valid (renew 30 mins before 8h expiry)
            if (!string.IsNullOrWhiteSpace(_cachedToken) && _tokenExpiry.HasValue && DateTime.UtcNow < _tokenExpiry.Value.AddMinutes(-30))
            {
                return _cachedToken;
            }

            var env = _config["ASPNETCORE_ENVIRONMENT"] ?? _config["Environment"];
            var isProd = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);

            var username = _config["Hms:Username"] ?? _config["HMS_USERNAME"] ?? (isProd ? null : "admin");
            var password = _config["Hms:Password"] ?? _config["HMS_PASSWORD"] ?? (isProd ? null : "Admin");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogError("HMS credentials are not configured.");
                return null;
            }

            var body = JsonSerializer.Serialize(new { username, password });
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken);
                var loginResp = JsonSerializer.Deserialize<HmsLoginResponseDto>(json);
                if (loginResp != null && !string.IsNullOrWhiteSpace(loginResp.Token))
                {
                    _cachedToken = loginResp.Token;
                    // Token lifetime is 8h
                    _tokenExpiry = DateTime.UtcNow.AddHours(8);
                    _consecutiveAuthFailures = 0;
                    _authLockoutUntil = null;
                    return _cachedToken;
                }
            }

            _consecutiveAuthFailures++;
            var errorContent = await res.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("HMS login failed with HTTP {Status}: {Error}. Consecutive failures: {Failures}", res.StatusCode, errorContent, _consecutiveAuthFailures);

            // HMS account lockout protection: 5 failures lock account for 15 min. Stop calling immediately.
            if (_consecutiveAuthFailures >= 3)
            {
                _authLockoutUntil = DateTime.UtcNow.AddMinutes(15);
                _logger.LogCritical("HMS login failed 3+ times. Backing off for 15 minutes to prevent HMS account freeze.");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception during HMS login.");
            return null;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var token = await LoginAsync(cancellationToken);
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return request;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var res = await _httpClient.SendAsync(request, cancellationToken);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Token may have expired or been revoked. Clear cached token, re-login, and retry once.
            _logger.LogWarning("HMS returned 401 Unauthorized. Clearing token and retrying once.");
            _cachedToken = null;
            var newToken = await LoginAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(newToken))
            {
                using var retryReq = new HttpRequestMessage(request.Method, request.RequestUri);
                retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                if (request.Content != null)
                {
                    var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                    retryReq.Content = new ByteArrayContent(bytes);
                    if (request.Content.Headers.ContentType != null)
                        retryReq.Content.Headers.ContentType = request.Content.Headers.ContentType;
                }
                res = await _httpClient.SendAsync(retryReq, cancellationToken);
            }
        }
        return res;
    }

    public async Task<HmsPatientDto?> SearchPatientByMrnAsync(string hmsMrn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hmsMrn))
            return null;

        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, $"api/reception/search?q={Uri.EscapeDataString(hmsMrn)}", cancellationToken);
        using var res = await SendWithRetryAsync(req, cancellationToken);

        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("patients", out var patientsElem) && patientsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in patientsElem.EnumerateArray())
            {
                var patient = JsonSerializer.Deserialize<HmsPatientDto>(item.GetRawText());
                if (patient != null && string.Equals(patient.GlobalId, hmsMrn, StringComparison.OrdinalIgnoreCase))
                {
                    return patient;
                }
            }
        }

        return null;
    }

    public async Task<HmsEmrBundleDto?> GetEmrAsync(int hmsPatientId, CancellationToken cancellationToken = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, $"api/clinical/{hmsPatientId}/emr", cancellationToken);
        using var res = await SendWithRetryAsync(req, cancellationToken);

        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<HmsEmrBundleDto>(json);
    }

    public async Task<List<HmsLabTestDto>> GetLabTestsAsync(int hmsPatientId, CancellationToken cancellationToken = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, $"api/lab/patient/{hmsPatientId}", cancellationToken);
        using var res = await SendWithRetryAsync(req, cancellationToken);

        if (!res.IsSuccessStatusCode)
            return new List<HmsLabTestDto>();

        var json = await res.Content.ReadAsStringAsync(cancellationToken);
        var resp = JsonSerializer.Deserialize<HmsLabResponseDto>(json);
        return resp?.Tests ?? new List<HmsLabTestDto>();
    }

    public async Task<List<HmsAppointmentDto>> GetAppointmentsAsync(int hmsPatientId, CancellationToken cancellationToken = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, $"api/appointments?patient_id={hmsPatientId}", cancellationToken);
        using var res = await SendWithRetryAsync(req, cancellationToken);

        if (!res.IsSuccessStatusCode)
            return new List<HmsAppointmentDto>();

        var json = await res.Content.ReadAsStringAsync(cancellationToken);
        var resp = JsonSerializer.Deserialize<HmsAppointmentsResponseDto>(json);
        return resp?.Appointments ?? new List<HmsAppointmentDto>();
    }

    public async Task<HmsAppointmentDto?> CreateAppointmentAsync(HmsCreateAppointmentRequestDto request, CancellationToken cancellationToken = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, "api/appointments", cancellationToken);
        var body = JsonSerializer.Serialize(request);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var res = await SendWithRetryAsync(req, cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("HMS CreateAppointment failed with status {Status}: {Error}", res.StatusCode, err);
            return null;
        }

        var json = await res.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("appointment", out var apptElem))
        {
            return JsonSerializer.Deserialize<HmsAppointmentDto>(apptElem.GetRawText());
        }

        return JsonSerializer.Deserialize<HmsAppointmentDto>(json);
    }

    public async Task<bool> UpdateAppointmentStatusAsync(int appointmentId, string status, CancellationToken cancellationToken = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Put, $"api/appointments/{appointmentId}/status", cancellationToken);
        var body = JsonSerializer.Serialize(new { status });
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var res = await SendWithRetryAsync(req, cancellationToken);
        return res.IsSuccessStatusCode;
    }

    public async Task<HmsRegisterPatientResponseDto?> RegisterPatientAsync(HmsRegisterPatientRequestDto request, CancellationToken cancellationToken = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, "api/reception/register", cancellationToken);
        var body = JsonSerializer.Serialize(request);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var res = await SendWithRetryAsync(req, cancellationToken);
        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<HmsRegisterPatientResponseDto>(json);
    }
}
