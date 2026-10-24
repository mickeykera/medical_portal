using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Tenancy;

namespace MedicalPortal.Web.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantService tenantService, ITenantContext tenantContext, ITokenService tokenService)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Exempt system, health, and documentation endpoints from strict tenant scoping
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/img/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        Tenant? tenant = null;

        // 1. Resolve by explicit header X-Tenant-Slug
        var slugHeader = context.Request.Headers[TenantConstants.TenantSlugHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(slugHeader))
        {
            tenant = await tenantService.GetTenantBySlugAsync(slugHeader, context.RequestAborted);
            if (tenant == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = $"Tenant with slug '{slugHeader}' not found.",
                    code = "tenant_not_found"
                }));
                return;
            }
        }

        // 2. Resolve by explicit header X-Tenant-Id
        if (tenant == null)
        {
            var idHeader = context.Request.Headers[TenantConstants.TenantIdHeader].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(idHeader) && Guid.TryParse(idHeader, out var tenantId))
            {
                tenant = await tenantService.GetTenantByIdAsync(tenantId, context.RequestAborted);
                if (tenant == null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = $"Tenant with ID '{tenantId}' not found.",
                        code = "tenant_not_found"
                    }));
                    return;
                }
            }
        }

        // 3. Resolve by Host header (domain or subdomain)
        if (tenant == null && context.Request.Host.HasValue)
        {
            tenant = await tenantService.GetTenantByHostAsync(context.Request.Host.Value, context.RequestAborted);
        }

        // 4. Fallback to default tenant (Springfield Health System) for local development, tests, or unspecified host
        if (tenant == null)
        {
            tenant = await tenantService.GetTenantByIdAsync(TenantConstants.DefaultTenantId, context.RequestAborted);
        }

        if (tenant == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Default hospital tenant is not provisioned or active.",
                code = "tenant_not_found"
            }));
            return;
        }

        if (!tenant.IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = $"Hospital tenant '{tenant.Name}' is suspended or inactive.",
                code = "tenant_inactive"
            }));
            return;
        }

        // 5. Server-side session to tenant binding check:
        // If request includes a Bearer token or session cookie, ensure credentials belong to this tenant
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        string? token = null;
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader.Substring("Bearer ".Length).Trim();
        }
        else
        {
            token = context.Request.Cookies["mp_session"];
        }

        if (!string.IsNullOrEmpty(token))
        {
            var claims = tokenService.ValidateAccessToken(token);
            if (claims != null && claims.TenantId.HasValue && claims.Role != MedicalPortal.Core.Enums.UserRole.PLATFORM_ADMIN)
            {
                var tokenTenantId = claims.TenantId.Value == Guid.Empty ? TenantConstants.DefaultTenantId : claims.TenantId.Value;
                if (tokenTenantId != tenant.Id)
                {
                    _logger.LogWarning("Cross-tenant access attempted: Account {AccountId} (Tenant {TokenTenantId}) attempted to access Tenant {TargetTenantId} ({TargetSlug}).",
                        claims.UserAccountId, tokenTenantId, tenant.Id, tenant.Slug);

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = "Cross-tenant access prohibited: your authentication session belongs to a different hospital tenant.",
                        code = "tenant_mismatch"
                    }));
                    return;
                }
            }
        }

        // Establish tenant context for the request
        tenantContext.SetTenant(tenant);
        context.Items["Tenant"] = tenant;
        context.Items["TenantId"] = tenant.Id;
        context.Response.Headers[TenantConstants.TenantSlugHeader] = tenant.Slug;

        await _next(context);
    }
}
