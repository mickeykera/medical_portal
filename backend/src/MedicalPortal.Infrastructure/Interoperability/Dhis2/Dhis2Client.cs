using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Interoperability.Dhis2;

public class Dhis2Client : IDhis2Client
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<Dhis2Options> _options;
    private readonly ILogger<Dhis2Client> _logger;

    public Dhis2Client(
        HttpClient httpClient,
        IOptions<Dhis2Options> options,
        ILogger<Dhis2Client>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger ?? NullLogger<Dhis2Client>.Instance;

        var baseUrl = _options.Value.BaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            _httpClient.BaseAddress = uri;
        }

        if (!string.IsNullOrWhiteSpace(_options.Value.Username) && !string.IsNullOrWhiteSpace(_options.Value.Password))
        {
            var authBytes = Encoding.UTF8.GetBytes($"{_options.Value.Username}:{_options.Value.Password}");
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/system/info", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DHIS2 ping failed.");
            return false;
        }
    }

    public async Task<Dhis2ImportSummaryDto> SubmitDataValueSetAsync(
        Dhis2DataValueSetDto payload,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/dataValueSets?dryRun={(dryRun ? "true" : "false")}";
        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var summary = JsonSerializer.Deserialize<Dhis2ImportSummaryDto>(
                    responseBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (summary != null) return summary;
            }
            catch
            {
                // Fallback parsing below
            }

            return new Dhis2ImportSummaryDto(
                Status: "SUCCESS",
                Description: "Import process completed successfully",
                ImportCount: new Dhis2ImportCountDto(payload.DataValues.Count, 0, 0, 0)
            );
        }

        return new Dhis2ImportSummaryDto(
            Status: "ERROR",
            Description: $"DHIS2 submission failed with status {(int)response.StatusCode}: {responseBody}",
            ImportCount: new Dhis2ImportCountDto(0, 0, payload.DataValues.Count, 0),
            Conflicts: new List<string> { responseBody }
        );
    }
}
