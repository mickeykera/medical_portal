using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Services;

public record PublicIpInfo(
    string IpAddress,
    bool IsPublic,
    string? City = null,
    string? Region = null,
    string? Country = null,
    string? CountryCode = null,
    double? Latitude = null,
    double? Longitude = null,
    string? Isp = null,
    string? Timezone = null,
    string Source = "default");

public record ClientIpDetails(
    string ClientIp,
    bool IsPrivateOrLoopback,
    string? PublicEgressIp,
    PublicIpInfo IpLocation,
    Dictionary<string, string?> ForwardedHeaders);

public interface IPublicIpService
{
    string GetClientIpAddress(HttpContext context);
    bool IsPrivateOrLoopback(string ipAddress);
    Task<string?> ResolvePublicEgressIpAsync(CancellationToken cancellationToken = default);
    Task<PublicIpInfo> ResolveIpLocationAsync(string? ipAddress = null, CancellationToken cancellationToken = default);
    Task<ClientIpDetails> GetClientIpDetailsAsync(HttpContext context, CancellationToken cancellationToken = default);
}

public class PublicIpService : IPublicIpService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PublicIpService> _logger;

    // Addis Ababa, Ethiopia clinic default fallback
    private const double DefaultLatitude = 9.0054;
    private const double DefaultLongitude = 38.7636;
    private const string DefaultCity = "Addis Ababa";
    private const string DefaultRegion = "Addis Ababa";
    private const string DefaultCountry = "ET";

    public PublicIpService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<PublicIpService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public string GetClientIpAddress(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;

        // SEC-17: Accept forwarded proxy headers only when the immediate peer is a verified trusted proxy or loopback.
        // Untrusted external peers cannot spoof their IP by sending arbitrary X-Forwarded-For or CF-Connecting-IP headers.
        var isTrustedProxy = remoteIp == null || IPAddress.IsLoopback(remoteIp);
        if (isTrustedProxy)
        {
            // 1. Cloudflare header
            var cfConnectingIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(cfConnectingIp) && IPAddress.TryParse(cfConnectingIp.Trim(), out _))
            {
                return cfConnectingIp.Trim();
            }

            // 2. X-Real-IP
            var xRealIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xRealIp) && IPAddress.TryParse(xRealIp.Trim(), out _))
            {
                return xRealIp.Trim();
            }

            // 3. X-Forwarded-For (leftmost is original client)
            var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xForwardedFor))
            {
                var firstIp = xForwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                          .Select(s => s.Trim())
                                          .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(firstIp))
                {
                    // Strip port if present (e.g. 192.168.1.1:54321)
                    if (firstIp.Contains(':') && !firstIp.Contains("::") && firstIp.IndexOf(':') == firstIp.LastIndexOf(':'))
                    {
                        firstIp = firstIp.Split(':')[0];
                    }

                    if (IPAddress.TryParse(firstIp, out _))
                    {
                        return firstIp;
                    }
                }
            }

            // 4. True-Client-IP
            var trueClientIp = context.Request.Headers["True-Client-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(trueClientIp) && IPAddress.TryParse(trueClientIp.Trim(), out _))
            {
                return trueClientIp.Trim();
            }
        }

        if (remoteIp != null)
        {
            if (remoteIp.IsIPv4MappedToIPv6)
            {
                remoteIp = remoteIp.MapToIPv4();
            }
            return remoteIp.ToString();
        }

        return "127.0.0.1";
    }

    public bool IsPrivateOrLoopback(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return true;

        if (!IPAddress.TryParse(ipAddress, out var ip))
            return true;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;

            // 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            // 169.254.0.0/16 Link-Local
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            // 0.0.0.0/8
            if (bytes[0] == 0)
                return true;

            // 127.0.0.0/8
            if (bytes[0] == 127)
                return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                return true;

            // Unique local addresses (fc00::/7)
            var bytes = ip.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc)
                return true;
        }

        return false;
    }

    public async Task<string?> ResolvePublicEgressIpAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "portal_resolved_public_egress_ip";
        if (_cache.TryGetValue(cacheKey, out string? cachedIp) && !string.IsNullOrWhiteSpace(cachedIp))
        {
            return cachedIp;
        }

        HttpClient? client = null;
        try
        {
            client = _httpClientFactory.CreateClient("PublicIpClient");
            if (client != null)
            {
                client.Timeout = TimeSpan.FromSeconds(3.5);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not create HttpClient from factory.");
        }

        if (client == null)
        {
            return null;
        }

        // Try primary provider: ipify
        try
        {
            var response = await client.GetStringAsync("https://api.ipify.org?format=json", cancellationToken);
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("ip", out var ipProp))
            {
                var resolved = ipProp.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(resolved) && IPAddress.TryParse(resolved, out _))
                {
                    _cache.Set(cacheKey, resolved, TimeSpan.FromMinutes(30));
                    return resolved;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve public IP via ipify.org, trying secondary provider.");
        }

        // Try secondary provider: icanhazip.com
        try
        {
            var response = await client.GetStringAsync("https://icanhazip.com", cancellationToken);
            var resolved = response.Trim();
            if (!string.IsNullOrWhiteSpace(resolved) && IPAddress.TryParse(resolved, out _))
            {
                _cache.Set(cacheKey, resolved, TimeSpan.FromMinutes(30));
                return resolved;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve public IP via icanhazip.com.");
        }

        // Return resolved IP from secure providers
        return null;
    }

    public async Task<PublicIpInfo> ResolveIpLocationAsync(string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        // If IP is not provided or is private/loopback, resolve server's public egress IP
        var targetIp = ipAddress;
        var isPublic = !string.IsNullOrWhiteSpace(targetIp) && !IsPrivateOrLoopback(targetIp);

        if (!isPublic)
        {
            var egressIp = await ResolvePublicEgressIpAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(egressIp) && !IsPrivateOrLoopback(egressIp))
            {
                targetIp = egressIp;
                isPublic = true;
            }
        }

        if (string.IsNullOrWhiteSpace(targetIp) || !isPublic)
        {
            return new PublicIpInfo(
                IpAddress: targetIp ?? "127.0.0.1",
                IsPublic: false,
                City: DefaultCity,
                Region: DefaultRegion,
                Country: DefaultCountry,
                CountryCode: "ET",
                Latitude: DefaultLatitude,
                Longitude: DefaultLongitude,
                Isp: "Local Network",
                Timezone: "Africa/Addis_Ababa",
                Source: "default-fallback"
            );
        }

        var cacheKey = $"portal_ip_geo_{targetIp}";
        if (_cache.TryGetValue(cacheKey, out PublicIpInfo? cachedInfo) && cachedInfo != null)
        {
            return cachedInfo;
        }

        // SEC-18: Plain-HTTP external geolocation calls (ip-api.com) have been dropped.
        // Returns safe default coordinates without cleartext network egress.
        var fallbackInfo = new PublicIpInfo(
            IpAddress: targetIp,
            IsPublic: true,
            City: DefaultCity,
            Region: DefaultRegion,
            Country: DefaultCountry,
            CountryCode: "ET",
            Latitude: DefaultLatitude,
            Longitude: DefaultLongitude,
            Isp: "Unknown",
            Timezone: "Africa/Addis_Ababa",
            Source: "default-fallback"
        );

        _cache.Set(cacheKey, fallbackInfo, TimeSpan.FromMinutes(10));
        return fallbackInfo;
    }

    public async Task<ClientIpDetails> GetClientIpDetailsAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var clientIp = GetClientIpAddress(context);
        var isPrivate = IsPrivateOrLoopback(clientIp);
        var publicEgress = await ResolvePublicEgressIpAsync(cancellationToken);
        var ipLocation = await ResolveIpLocationAsync(isPrivate ? publicEgress : clientIp, cancellationToken);

        var forwardedHeaders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CF-Connecting-IP"] = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault(),
            ["X-Real-IP"] = context.Request.Headers["X-Real-IP"].FirstOrDefault(),
            ["X-Forwarded-For"] = context.Request.Headers["X-Forwarded-For"].FirstOrDefault(),
            ["True-Client-IP"] = context.Request.Headers["True-Client-IP"].FirstOrDefault(),
            ["Forwarded"] = context.Request.Headers["Forwarded"].FirstOrDefault(),
            ["RemoteIpAddress"] = context.Connection.RemoteIpAddress?.ToString()
        };

        return new ClientIpDetails(
            ClientIp: clientIp,
            IsPrivateOrLoopback: isPrivate,
            PublicEgressIp: publicEgress,
            IpLocation: ipLocation,
            ForwardedHeaders: forwardedHeaders
        );
    }
}
