using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Tenancy;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ITenantContext _tenantContext;

    public TenantController(ITenantService tenantService, ITenantContext tenantContext)
    {
        _tenantService = tenantService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns current active tenant configuration, branding tokens, and enabled modules.
    /// </summary>
    [HttpGet("current")]
    public IActionResult GetCurrentTenant()
    {
        var tenant = _tenantContext.CurrentTenant;
        if (tenant == null)
            return NotFound(new { error = "No hospital tenant is currently resolved." });

        var settings = tenant.GetParsedSettings();

        return Ok(new
        {
            id = tenant.Id,
            name = tenant.Name,
            slug = tenant.Slug,
            region = tenant.Region,
            timeZone = tenant.TimeZone,
            languages = JsonSerializer.Deserialize<List<string>>(tenant.LanguagesJson),
            branding = settings.Branding,
            modules = settings.Modules,
            hosting = settings.Hosting,
            integrations = settings.Integrations,
            security = settings.Security,
            currency = settings.Currency,
            location = settings.Location
        });
    }

    /// <summary>
    /// Returns branding design tokens and WCAG 2.1 AA color contrast compliance calculation.
    /// </summary>
    [HttpGet("current/theme")]
    public IActionResult GetThemeTokens()
    {
        var tenant = _tenantContext.CurrentTenant;
        if (tenant == null)
            return NotFound(new { error = "No hospital tenant is currently resolved." });

        var settings = tenant.GetParsedSettings();
        var primaryColor = settings.Branding.PrimaryColor;

        var contrast = WcagColorContrastCalculator.AnalyzeHexColor(primaryColor);

        return Ok(new
        {
            tenantName = tenant.Name,
            slug = tenant.Slug,
            tokens = new
            {
                primaryColor,
                logo = settings.Branding.Logo,
                supportEmail = settings.Branding.SupportEmail
            },
            wcagCompliance = new
            {
                ratioAgainstWhite = contrast.RatioAgainstWhite,
                ratioAgainstBlack = contrast.RatioAgainstBlack,
                passesAaNormalText = contrast.PassesAaNormalWhite,
                passesAaLargeText = contrast.PassesAaLargeWhite,
                evaluation = contrast.PassesAaNormalWhite
                    ? "Passes WCAG 2.1 AA requirements (contrast ratio >= 4.5:1 against white)."
                    : "Fails WCAG 2.1 AA requirements for normal text (< 4.5:1 against white)."
            }
        });
    }

    /// <summary>
    /// Provisions a new hospital tenant with isolated encryption key and tamper-evident audit chain.
    /// Restricted to platform administrators.
    /// </summary>
    [HttpPost("/api/platform/tenants/provision")]
    [RequireAuth]
    public async Task<IActionResult> ProvisionTenant([FromBody] TenantProvisioningRequest request)
    {
        var actor = HttpContext.Items["Actor"] as AccessTokenClaims;
        if (actor == null || (actor.Role != UserRole.PLATFORM_ADMIN && actor.Role != UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Tenant provisioning is restricted to platform administrators."
            });
        }

        try
        {
            var tenant = await _tenantService.ProvisionTenantAsync(request, HttpContext.RequestAborted);
            return StatusCode(StatusCodes.Status201Created, new
            {
                tenant = new
                {
                    id = tenant.Id,
                    name = tenant.Name,
                    slug = tenant.Slug,
                    region = tenant.Region,
                    timeZone = tenant.TimeZone,
                    masterKeyFingerprint = tenant.MasterKeyFingerprint,
                    auditChainHash = tenant.AuditChainHash,
                    createdAt = tenant.CreatedAt
                },
                message = $"Hospital tenant '{tenant.Name}' ({tenant.Slug}) successfully provisioned."
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Validates tenant configuration against schema, module rules, and WCAG 2.1 AA color contrast without persisting.
    /// Restricted to platform administrators.
    /// </summary>
    [HttpPost("/api/platform/tenants/validate")]
    [RequireAuth]
    public IActionResult ValidateTenantConfig([FromBody] TenantProvisioningRequest request)
    {
        var actor = HttpContext.Items["Actor"] as AccessTokenClaims;
        if (actor == null || (actor.Role != UserRole.PLATFORM_ADMIN && actor.Role != UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Tenant validation is restricted to platform administrators."
            });
        }

        var result = TenantConfigurationValidator.Validate(
            request.Name,
            request.Slug,
            request.Region,
            request.TimeZone,
            request.Languages,
            request.Settings,
            request.FacilitiesCount,
            request.Departments);

        return Ok(new
        {
            isValid = result.IsValid,
            errors = result.Errors,
            warnings = result.Warnings,
            wcag = result.WcagAnalysis
        });
    }

    /// <summary>
    /// Retrieves full tenant configuration, facilities, and settings. Restricted to platform administrators.
    /// </summary>
    [HttpGet("/api/platform/tenants/{id:guid}")]
    [RequireAuth]
    public async Task<IActionResult> GetTenantDetails(Guid id)
    {
        var actor = HttpContext.Items["Actor"] as AccessTokenClaims;
        if (actor == null || (actor.Role != UserRole.PLATFORM_ADMIN && actor.Role != UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Access is restricted to platform administrators."
            });
        }

        var tenant = await _tenantService.GetTenantByIdAsync(id, HttpContext.RequestAborted);
        if (tenant == null) return NotFound(new { error = $"Tenant with ID '{id}' not found." });

        var settings = tenant.GetParsedSettings();
        return Ok(new
        {
            id = tenant.Id,
            name = tenant.Name,
            slug = tenant.Slug,
            hostDomain = tenant.HostDomain,
            region = tenant.Region,
            timeZone = tenant.TimeZone,
            languages = JsonSerializer.Deserialize<List<string>>(tenant.LanguagesJson),
            isActive = tenant.IsActive,
            masterKeyFingerprint = tenant.MasterKeyFingerprint,
            auditChainHash = tenant.AuditChainHash,
            createdAt = tenant.CreatedAt,
            updatedAt = tenant.UpdatedAt,
            settings
        });
    }

    /// <summary>
    /// Updates tenant configuration settings (branding, modules, hosting, security). Restricted to platform administrators.
    /// </summary>
    [HttpPut("/api/platform/tenants/{id:guid}/settings")]
    [RequireAuth]
    public async Task<IActionResult> UpdateTenantSettings(Guid id, [FromBody] TenantSettings settings)
    {
        var actor = HttpContext.Items["Actor"] as AccessTokenClaims;
        if (actor == null || (actor.Role != UserRole.PLATFORM_ADMIN && actor.Role != UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Updating tenant settings is restricted to platform administrators."
            });
        }

        try
        {
            var updated = await _tenantService.UpdateTenantSettingsAsync(id, settings, HttpContext.RequestAborted);
            if (updated == null) return NotFound(new { error = $"Tenant with ID '{id}' not found." });

            return Ok(new
            {
                message = $"Tenant '{updated.Name}' settings successfully updated.",
                tenantId = updated.Id,
                settings = updated.GetParsedSettings()
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public record ToggleModuleDto(string Module, bool Enabled);

    /// <summary>
    /// Dynamically toggles a tenant feature module on or off with immediate API-level gating effect.
    /// Restricted to platform administrators.
    /// </summary>
    [HttpPatch("/api/platform/tenants/{id:guid}/modules")]
    [RequireAuth]
    public async Task<IActionResult> ToggleTenantModule(Guid id, [FromBody] ToggleModuleDto request)
    {
        var actor = HttpContext.Items["Actor"] as AccessTokenClaims;
        if (actor == null || (actor.Role != UserRole.PLATFORM_ADMIN && actor.Role != UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Toggling tenant modules is restricted to platform administrators."
            });
        }

        try
        {
            var updated = await _tenantService.ToggleTenantModuleAsync(id, request.Module, request.Enabled, HttpContext.RequestAborted);
            if (updated == null) return NotFound(new { error = $"Tenant with ID '{id}' not found." });

            return Ok(new
            {
                message = $"Module '{request.Module}' {(request.Enabled ? "enabled" : "disabled")} for tenant '{updated.Name}'.",
                tenantId = updated.Id,
                modules = updated.GetParsedSettings().Modules
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Exports portable tenant configuration JSON for backups or migration. Restricted to platform administrators.
    /// </summary>
    [HttpGet("/api/platform/tenants/{id:guid}/export-config")]
    [RequireAuth]
    public async Task<IActionResult> ExportTenantConfig(Guid id)
    {
        var actor = HttpContext.Items["Actor"] as AccessTokenClaims;
        if (actor == null || (actor.Role != UserRole.PLATFORM_ADMIN && actor.Role != UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Exporting tenant configuration is restricted to platform administrators."
            });
        }

        var tenant = await _tenantService.GetTenantByIdAsync(id, HttpContext.RequestAborted);
        if (tenant == null) return NotFound(new { error = $"Tenant with ID '{id}' not found." });

        var export = new
        {
            name = tenant.Name,
            slug = tenant.Slug,
            hostDomain = tenant.HostDomain,
            region = tenant.Region,
            timeZone = tenant.TimeZone,
            languages = JsonSerializer.Deserialize<List<string>>(tenant.LanguagesJson),
            settings = tenant.GetParsedSettings()
        };

        return Ok(export);
    }

    /// <summary>
    /// Lists all hospital tenants in the cluster. Restricted to platform administrators.
    /// </summary>
    [HttpGet("/api/platform/tenants")]
    [RequireAuth]
    public async Task<IActionResult> GetAllTenants()
    {
        var actor = HttpContext.Items["Actor"] as AccessTokenClaims;
        if (actor == null || (actor.Role != UserRole.PLATFORM_ADMIN && actor.Role != UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Listing all hospital tenants is restricted to platform administrators."
            });
        }

        var tenants = await _tenantService.GetAllTenantsAsync(HttpContext.RequestAborted);
        return Ok(tenants.Select(t => new
        {
            id = t.Id,
            name = t.Name,
            slug = t.Slug,
            hostDomain = t.HostDomain,
            region = t.Region,
            timeZone = t.TimeZone,
            isActive = t.IsActive,
            createdAt = t.CreatedAt
        }));
    }
}
