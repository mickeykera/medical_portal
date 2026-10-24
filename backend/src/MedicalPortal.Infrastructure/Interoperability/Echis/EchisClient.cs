using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Interoperability.Echis;

public class EchisClient : IEchisClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<EchisOptions> _options;
    private readonly ILogger<EchisClient> _logger;

    public EchisClient(
        HttpClient httpClient,
        IOptions<EchisOptions> options,
        ILogger<EchisClient>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger ?? NullLogger<EchisClient>.Instance;

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

    public async Task<EchisReferralResponseDto> SendReferralAsync(EchisReferralDto referral, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("referrals", referral, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new EchisReferralResponseDto(true, referral.ReferralNumber, "Successfully posted to eCHIS gateway.");
        }

        var err = await response.Content.ReadAsStringAsync(cancellationToken);
        return new EchisReferralResponseDto(false, referral.ReferralNumber, $"eCHIS error {(int)response.StatusCode}: {err}");
    }

    public async Task<IReadOnlyList<EchisReferralDto>> FetchInboundReferralsAsync(string facilityCode, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"referrals/inbound?facility={facilityCode}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var referrals = await response.Content.ReadFromJsonAsync<List<EchisReferralDto>>(cancellationToken: cancellationToken);
        return referrals ?? new List<EchisReferralDto>();
    }

    public async Task<bool> UpdateReferralStatusAsync(string referralNumber, string status, string? notes = null, CancellationToken cancellationToken = default)
    {
        var payload = new { status, notes };
        var response = await _httpClient.PutAsJsonAsync($"referrals/{referralNumber}/status", payload, cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
