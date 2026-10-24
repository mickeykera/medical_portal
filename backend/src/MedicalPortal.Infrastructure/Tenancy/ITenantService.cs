using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Tenancy;

public record TenantProvisioningRequest(
    string Name,
    string Slug,
    string Region,
    string TimeZone,
    List<string> Languages,
    TenantSettings Settings,
    int FacilitiesCount = 1,
    List<string>? Departments = null,
    string? HostDomain = null);

public interface ITenantService
{
    Task<Tenant?> GetTenantBySlugAsync(string slug, CancellationToken ct = default);
    Task<Tenant?> GetTenantByIdAsync(Guid id, CancellationToken ct = default);
    Task<Tenant?> GetTenantByHostAsync(string host, CancellationToken ct = default);
    Task<Tenant> ProvisionTenantAsync(TenantProvisioningRequest request, CancellationToken ct = default);
    Task<Tenant?> UpdateTenantSettingsAsync(Guid id, TenantSettings settings, CancellationToken ct = default);
    Task<Tenant?> ToggleTenantModuleAsync(Guid id, string moduleName, bool enable, CancellationToken ct = default);
    Task<List<Tenant>> GetAllTenantsAsync(CancellationToken ct = default);
}

public class TenantService : ITenantService
{
    private readonly MedicalPortalDbContext _db;
    private readonly ILogger<TenantService> _logger;

    public TenantService(MedicalPortalDbContext db, ILogger<TenantService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Tenant?> GetTenantBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var normalized = slug.Trim().ToLowerInvariant();

        return await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug.ToLower() == normalized, ct);
    }

    public async Task<Tenant?> GetTenantByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<Tenant?> GetTenantByHostAsync(string host, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;

        // Clean host (remove port if present)
        var cleanHost = host.Split(':')[0].Trim().ToLowerInvariant();

        // 1. Direct host domain match
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.HostDomain != null && t.HostDomain.ToLower() == cleanHost, ct);

        if (tenant != null) return tenant;

        // 2. Subdomain extraction: e.g. "adot-hospital.clinic.example" -> slug "adot-hospital"
        var parts = cleanHost.Split('.');
        if (parts.Length > 2)
        {
            var potentialSlug = parts[0].ToLowerInvariant();
            tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Slug.ToLower() == potentialSlug, ct);
        }

        return tenant;
    }

    public async Task<List<Tenant>> GetAllTenantsAsync(CancellationToken ct = default)
    {
        return await _db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    public async Task<Tenant> ProvisionTenantAsync(TenantProvisioningRequest request, CancellationToken ct = default)
    {
        // 1. Schema & business rule validation
        var validation = TenantConfigurationValidator.Validate(
            request.Name,
            request.Slug,
            request.Region,
            request.TimeZone,
            request.Languages,
            request.Settings,
            request.FacilitiesCount,
            request.Departments);

        if (!validation.IsValid)
        {
            throw new ArgumentException($"Tenant configuration validation failed: {string.Join("; ", validation.Errors)}");
        }

        var normalizedSlug = request.Slug.Trim();

        var existing = await _db.Tenants
            .AnyAsync(t => t.Slug.ToLower() == normalizedSlug.ToLower(), ct);

        if (existing)
        {
            throw new InvalidOperationException($"Tenant with slug '{normalizedSlug}' already exists.");
        }

        // Cryptographic key fingerprint
        var keyFingerprintBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{normalizedSlug}-{Guid.NewGuid():N}-{DateTime.UtcNow.Ticks}"));
        var keyFingerprint = $"fp_{Convert.ToHexString(keyFingerprintBytes)[..16].ToLowerInvariant()}";

        // Per-tenant audit chain genesis hash
        var genesisBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"GENESIS-TENANT-{normalizedSlug}-{DateTime.UtcNow:O}"));
        var genesisHash = Convert.ToHexString(genesisBytes).ToLowerInvariant();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Slug = normalizedSlug,
            HostDomain = request.HostDomain?.Trim().ToLowerInvariant(),
            Region = request.Region ?? "Other: Ethiopia, Addis Ababa",
            TimeZone = request.TimeZone ?? "UTC",
            LanguagesJson = JsonSerializer.Serialize(request.Languages ?? new List<string> { "en" }),
            IsActive = true,
            MasterKeyFingerprint = keyFingerprint,
            AuditChainHash = genesisHash,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        tenant.SetSettings(request.Settings ?? new TenantSettings());

        _db.Tenants.Add(tenant);

        // Seed initial organization and facility if requested
        if (request.FacilitiesCount > 0)
        {
            var org = new Organization
            {
                TenantId = tenant.Id,
                Name = tenant.Name,
                Code = $"{normalizedSlug.Replace("-", "").ToUpperInvariant()}_{tenant.Id.ToString("N")[..6]}"
            };
            _db.Organizations.Add(org);

            for (int f = 1; f <= request.FacilitiesCount; f++)
            {
                var facility = new Facility
                {
                    TenantId = tenant.Id,
                    Organization = org,
                    Name = $"{tenant.Name} Facility {f}",
                    Code = $"{org.Code}_FAC_{f}"
                };
                _db.Facilities.Add(facility);

                if (request.Departments != null && request.Departments.Count > 0)
                {
                    foreach (var deptName in request.Departments)
                    {
                        var cleanDept = deptName.Trim();
                        if (cleanDept.StartsWith("and ", StringComparison.OrdinalIgnoreCase))
                        {
                            cleanDept = cleanDept.Substring(4).Trim();
                        }

                        var deptCode = cleanDept.ToUpperInvariant().Replace(" ", "_").Replace("&", "AND");
                        if (deptCode.Length > 12) deptCode = deptCode[..12];

                        var dept = new Department
                        {
                            TenantId = tenant.Id,
                            Facility = facility,
                            Name = cleanDept,
                            Code = deptCode,
                            Specialty = cleanDept
                        };
                        _db.Departments.Add(dept);

                        // Seed default weekly schedule templates
                        for (int d = 1; d <= 5; d++)
                        {
                            _db.WeeklyScheduleTemplates.Add(new WeeklyScheduleTemplate
                            {
                                TenantId = tenant.Id,
                                DayOfWeek = (DayOfWeek)d,
                                StartTime = new TimeSpan(8, 0, 0),
                                EndTime = new TimeSpan(17, 0, 0),
                                IsActive = true
                            });
                        }
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant {TenantName} ({Slug}) successfully provisioned with ID {TenantId} and key fingerprint {KeyFingerprint}.",
            tenant.Name, tenant.Slug, tenant.Id, tenant.MasterKeyFingerprint);

        return tenant;
    }

    public async Task<Tenant?> UpdateTenantSettingsAsync(Guid id, TenantSettings settings, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant == null) return null;

        var languages = JsonSerializer.Deserialize<List<string>>(tenant.LanguagesJson) ?? new List<string> { "en" };

        var validation = TenantConfigurationValidator.Validate(
            tenant.Name,
            tenant.Slug,
            tenant.Region,
            tenant.TimeZone,
            languages,
            settings,
            1,
            null);

        if (!validation.IsValid)
        {
            throw new ArgumentException($"Tenant settings validation failed: {string.Join("; ", validation.Errors)}");
        }

        tenant.SetSettings(settings);
        tenant.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant settings updated for {TenantName} ({TenantId}).", tenant.Name, tenant.Id);
        return tenant;
    }

    public async Task<Tenant?> ToggleTenantModuleAsync(Guid id, string moduleName, bool enable, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name is required.", nameof(moduleName));

        var normalized = moduleName.Trim().ToLowerInvariant();
        if (!TenantModules.All.Contains(normalized))
            throw new ArgumentException($"Unknown system module '{moduleName}'.", nameof(moduleName));

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant == null) return null;

        var settings = tenant.GetParsedSettings();
        if (enable)
        {
            settings.Modules.Disabled.RemoveAll(d => d.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (!settings.Modules.Enabled.Any(e => e.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                settings.Modules.Enabled.Add(normalized);
            }
        }
        else
        {
            settings.Modules.Enabled.RemoveAll(e => e.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (!settings.Modules.Disabled.Any(d => d.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                settings.Modules.Disabled.Add(normalized);
            }
        }

        tenant.SetSettings(settings);
        tenant.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant module '{ModuleName}' set to {State} for {TenantName} ({TenantId}).",
            normalized, enable ? "enabled" : "disabled", tenant.Name, tenant.Id);
        return tenant;
    }
}
