using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Offline;

public class CloudAggregatorClient : ICloudAggregatorClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<FacilityNodeOptions> _options;
    private readonly ILogger<CloudAggregatorClient> _logger;

    public CloudAggregatorClient(
        HttpClient httpClient,
        IOptions<FacilityNodeOptions> options,
        ILogger<CloudAggregatorClient>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger ?? NullLogger<CloudAggregatorClient>.Instance;

        var baseUrl = _options.Value.AggregatorEndpoint;
        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            _httpClient.BaseAddress = uri;
        }

        if (!string.IsNullOrWhiteSpace(_options.Value.AggregatorApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.Value.AggregatorApiKey);
            _httpClient.DefaultRequestHeaders.Add("X-Facility-Code", _options.Value.FacilityCode);
        }
    }

    public async Task<bool> PingAggregatorAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Aggregator ping failed.");
            return false;
        }
    }

    public async Task<AggregatorDispatchResultDto> DispatchMessageAsync(AggregatorDispatchItemDto item, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "events")
            {
                Content = JsonContent.Create(item)
            };
            request.Headers.Add("Idempotency-Key", item.IdempotencyKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                string ackId = $"ACK-{Guid.NewGuid():N}";
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("ackId", out var ackProp))
                    {
                        ackId = ackProp.GetString() ?? ackId;
                    }
                }
                catch
                {
                    // Default ack fallback
                }

                return new AggregatorDispatchResultDto(item.IdempotencyKey, true, ackId, statusCode, null);
            }

            if (statusCode == 409)
            {
                return new AggregatorDispatchResultDto(
                    item.IdempotencyKey,
                    false,
                    $"ACK-DEDUP-{item.IdempotencyKey}",
                    409,
                    "Duplicate idempotency key already recorded on aggregator."
                );
            }

            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new AggregatorDispatchResultDto(item.IdempotencyKey, false, null, statusCode, errBody);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Network connectivity error while contacting aggregator: {Message}", ex.Message);
            return new AggregatorDispatchResultDto(item.IdempotencyKey, false, null, 0, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error dispatching message {Key}", item.IdempotencyKey);
            return new AggregatorDispatchResultDto(item.IdempotencyKey, false, null, 500, ex.Message);
        }
    }

    public async Task<AggregatorBatchResultDto> DispatchBatchAsync(IReadOnlyList<AggregatorDispatchItemDto> items, CancellationToken cancellationToken = default)
    {
        var results = new List<AggregatorDispatchResultDto>();
        bool overall = true;

        foreach (var item in items)
        {
            var res = await DispatchMessageAsync(item, cancellationToken);
            results.Add(res);
            if (!res.Success && res.StatusCode != 409)
            {
                overall = false;
            }
        }

        return new AggregatorBatchResultDto(overall, results);
    }

    public async Task<IReadOnlyList<ReferencePackageDto>> FetchReferencePackagesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("reference-packages", cancellationToken);
        response.EnsureSuccessStatusCode();

        var packages = await response.Content.ReadFromJsonAsync<List<ReferencePackageDto>>(cancellationToken: cancellationToken);
        return packages ?? new List<ReferencePackageDto>();
    }
}
