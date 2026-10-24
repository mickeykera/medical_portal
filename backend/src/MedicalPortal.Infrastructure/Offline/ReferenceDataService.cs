using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Offline;

public class ReferenceDataService : IReferenceDataService
{
    private readonly MedicalPortalDbContext _db;
    private readonly ICloudAggregatorClient _aggregatorClient;
    private readonly IOptions<FacilityNodeOptions> _options;
    private readonly ILogger<ReferenceDataService> _logger;

    public ReferenceDataService(
        MedicalPortalDbContext db,
        ICloudAggregatorClient aggregatorClient,
        IOptions<FacilityNodeOptions> options,
        ILogger<ReferenceDataService>? logger = null)
    {
        _db = db;
        _aggregatorClient = aggregatorClient;
        _options = options;
        _logger = logger ?? NullLogger<ReferenceDataService>.Instance;
    }

    public async Task<ReferencePackageInstallResultDto> ValidateAndInstallPackageAsync(
        ReferencePackageDto dto,
        CancellationToken cancellationToken = default)
    {
        // 1. Verify content hash
        var computedHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(dto.PayloadJson));
        var computedHash = Convert.ToHexString(computedHashBytes).ToLowerInvariant();
        if (!string.Equals(computedHash, dto.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Reference package {PackageId} content hash mismatch. Corrupted payload.", dto.PackageId);
            return new ReferencePackageInstallResultDto(false, dto.PackageId, dto.Version, "Content hash mismatch. Package payload may be corrupted.");
        }

        // 2. Verify HMAC-SHA256 signature using constant-time comparison
        var secret = _options.Value.ReferencePackageSecret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedSigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{dto.PackageId}:{dto.VersionOrdinal}:{computedHash}"));
        byte[] actualSigBytes;
        try
        {
            actualSigBytes = Convert.FromHexString(dto.Signature);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Reference package {PackageId} signature format is invalid hex.", dto.PackageId);
            return new ReferencePackageInstallResultDto(false, dto.PackageId, dto.Version, "Invalid signature format.");
        }

        if (expectedSigBytes.Length != actualSigBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedSigBytes, actualSigBytes))
        {
            _logger.LogWarning("Reference package {PackageId} signature verification failed. Untrusted package.", dto.PackageId);
            return new ReferencePackageInstallResultDto(false, dto.PackageId, dto.Version, "Cryptographic signature verification failed. Untrusted package.");
        }

        // 3. Monotonic version anti-downgrade check
        var existingActive = await _db.ReferencePackages
            .Where(p => p.PackageId == dto.PackageId && p.IsActive)
            .OrderByDescending(p => p.VersionOrdinal)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingActive != null && existingActive.VersionOrdinal >= dto.VersionOrdinal)
        {
            _logger.LogWarning(
                "Reference package {PackageId} downgrade rejected: installed version ordinal {Current} >= incoming {Incoming}.",
                dto.PackageId, existingActive.VersionOrdinal, dto.VersionOrdinal);
            return new ReferencePackageInstallResultDto(
                false, dto.PackageId, dto.Version,
                $"Version downgrade rejected: installed version ordinal {existingActive.VersionOrdinal} ({existingActive.Version}) >= incoming {dto.VersionOrdinal} ({dto.Version}).");
        }

        // 4. Deactivate prior versions
        var existingPackages = await _db.ReferencePackages
            .Where(p => p.PackageId == dto.PackageId && p.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var p in existingPackages)
        {
            p.IsActive = false;
            p.Status = ReferencePackageStatus.Deprecated;
        }

        // 5. Install new package
        var tenantId = _db.CurrentTenantId ?? TenantConstants.DefaultTenantId;
        var entity = new ReferencePackage
        {
            TenantId = tenantId,
            PackageId = dto.PackageId,
            Name = dto.Name,
            Version = dto.Version,
            VersionOrdinal = dto.VersionOrdinal,
            PayloadJson = dto.PayloadJson,
            ContentHash = computedHash,
            Signature = dto.Signature,
            SignedBy = dto.SignedBy,
            SignedAtUtc = dto.SignedAtUtc,
            InstalledAtUtc = DateTime.UtcNow,
            Status = ReferencePackageStatus.Installed,
            IsActive = true
        };

        _db.ReferencePackages.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully installed reference package {PackageId} version {Version}.", dto.PackageId, dto.Version);
        return new ReferencePackageInstallResultDto(true, dto.PackageId, dto.Version, null);
    }

    public async Task<IReadOnlyList<ReferencePackageInstallResultDto>> PullAndApplyOnlinePackagesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ReferencePackageInstallResultDto>();
        try
        {
            var packages = await _aggregatorClient.FetchReferencePackagesAsync(cancellationToken);
            foreach (var pkg in packages)
            {
                var res = await ValidateAndInstallPackageAsync(pkg, cancellationToken);
                results.Add(res);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pull online reference packages from aggregator.");
        }

        return results;
    }

    public async Task<ReferencePackageInstallResultDto> ImportPackageBundleJsonAsync(string bundleJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var pkgDto = JsonSerializer.Deserialize<ReferencePackageDto>(bundleJson, options);
            if (pkgDto == null)
            {
                return new ReferencePackageInstallResultDto(false, "unknown", "unknown", "Malformed package JSON.");
            }

            return await ValidateAndInstallPackageAsync(pkgDto, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse reference package bundle JSON.");
            return new ReferencePackageInstallResultDto(false, "unknown", "unknown", $"JSON parse error: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<ReferencePackage>> GetActivePackagesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.ReferencePackages
            .Where(p => p.IsActive)
            .OrderBy(p => p.PackageId)
            .ToListAsync(cancellationToken);
    }
}
