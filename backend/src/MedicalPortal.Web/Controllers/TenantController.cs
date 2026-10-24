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
            languages = System.Text.Json.JsonSerializer.Deserialize<List<string>>(tenant.LanguagesJson),
            branding = settings.Branding,
            modules = settings.Modules,
            hosting = settings.Hosting,
            integrations = settings.Integrations,
            security = settings.Security
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
            region = t.Region,
            timeZone = t.TimeZone,
            isActive = t.IsActive,
            createdAt = t.CreatedAt
        }));
    }
}
