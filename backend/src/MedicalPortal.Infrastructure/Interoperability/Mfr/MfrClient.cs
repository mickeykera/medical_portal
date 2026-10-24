using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Interoperability.Mfr;

public class MfrClient : IMfrClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<MfrOptions> _options;
    private readonly ILogger<MfrClient> _logger;

    public MfrClient(
        HttpClient httpClient,
        IOptions<MfrOptions> options,
        ILogger<MfrClient>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger ?? NullLogger<MfrClient>.Instance;

        var baseUrl = _options.Value.BaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            _httpClient.BaseAddress = uri;
        }

        if (!string.IsNullOrWhiteSpace(_options.Value.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.Value.ApiKey);
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpClient.GetAsync("health", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MFR ping failed.");
            return false;
        }
    }

    public async Task<IReadOnlyList<MfrFacilityDto>> FetchFacilitiesAsync(CancellationToken cancellationToken = default)
    {
        var res = await _httpClient.GetAsync("facilities", cancellationToken);
        res.EnsureSuccessStatusCode();

        var list = await res.Content.ReadFromJsonAsync<List<MfrFacilityDto>>(cancellationToken: cancellationToken);
        return list ?? new List<MfrFacilityDto>();
    }

    public async Task<MfrFacilityDto?> GetFacilityByMfrIdAsync(string mfrId, CancellationToken cancellationToken = default)
    {
        var res = await _httpClient.GetAsync($"facilities/{mfrId}", cancellationToken);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();

        return await res.Content.ReadFromJsonAsync<MfrFacilityDto>(cancellationToken: cancellationToken);
    }
}
