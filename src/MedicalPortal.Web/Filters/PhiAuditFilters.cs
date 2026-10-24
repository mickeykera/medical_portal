using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MedicalPortal.Web.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class AuditPhiReadAttribute : Attribute
{
    public string? ResourceType { get; }

    public AuditPhiReadAttribute(string? resourceType = null)
    {
        ResourceType = resourceType;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class NoAuditPhiAttribute : Attribute
{
}

public class PhiAuditFilter : IAsyncActionFilter
{
    private readonly IAuditService _audit;
    private readonly ILogger<PhiAuditFilter> _logger;

    public PhiAuditFilter(IAuditService audit, ILogger<PhiAuditFilter> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            await next();
            return;
        }

        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<NoAuditPhiAttribute>() != null)
        {
            await next();
            return;
        }

        var explicitAttr = endpoint?.Metadata.GetMetadata<AuditPhiReadAttribute>();
        string? resourceType = explicitAttr?.ResourceType ?? InferResourceType(context.HttpContext.Request.Path.Value);

        if (resourceType == null)
        {
            await next();
            return;
        }

        var executedContext = await next();

        if (executedContext.Exception != null && !executedContext.ExceptionHandled)
            return;

        var statusCode = executedContext.HttpContext.Response.StatusCode;
        if (executedContext.Result is ObjectResult objRes && objRes.StatusCode.HasValue)
            statusCode = objRes.StatusCode.Value;
        else if (executedContext.Result is StatusCodeResult statusRes)
            statusCode = statusRes.StatusCode;

        if (statusCode < 200 || statusCode >= 300)
            return;

        try
        {
            var actor = executedContext.HttpContext.Items["Actor"] as AccessTokenClaims;
            var actorId = actor?.UserAccountId;

            Guid? patientId = null;
            if (executedContext.HttpContext.Items.TryGetValue("TargetPatientId", out var pidObj) && pidObj is Guid g)
            {
                patientId = g;
            }
            else if (executedContext.RouteData.Values.TryGetValue("id", out var idVal) && Guid.TryParse(idVal?.ToString(), out var parsedId))
            {
                patientId = parsedId;
            }
            else if (executedContext.HttpContext.Request.Query.TryGetValue("patient", out var patientQueryVal) && Guid.TryParse(patientQueryVal.FirstOrDefault(), out var parsedQueryId))
            {
                patientId = parsedQueryId;
            }
            else if (executedContext.HttpContext.Request.Headers.TryGetValue("x-patient-id", out var headerVal) && Guid.TryParse(headerVal.FirstOrDefault(), out var parsedHeaderId))
            {
                patientId = parsedHeaderId;
            }

            var isProxy = (executedContext.HttpContext.Items["IsProxy"] as bool?) ?? false;
            var isBreakGlass = (executedContext.HttpContext.Items["IsBreakGlass"] as bool?) ?? false;
            var actingForPersonId = executedContext.HttpContext.Items["ActingForPersonId"] as Guid?;

            string? resourceId = null;
            if (executedContext.RouteData.Values.TryGetValue("id", out var rId))
            {
                resourceId = rId?.ToString();
            }

            var ipAddress = executedContext.HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = executedContext.HttpContext.Request.Headers.UserAgent.ToString();

            await _audit.EnqueueLogAsync(
                action: isBreakGlass ? "PHI_READ_BREAK_GLASS" : "PHI_READ",
                resourceType: resourceType,
                resourceId: resourceId,
                actorUserAccountId: actorId,
                patientId: patientId,
                actingForPersonId: actingForPersonId,
                metadata: new { path = context.HttpContext.Request.Path.Value, breakGlass = isBreakGlass },
                ipAddress: ipAddress,
                userAgent: userAgent,
                isProxy: isProxy
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue PHI read audit log.");
        }
    }

    private static string? InferResourceType(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var normalized = path.ToLowerInvariant();

        if (normalized.StartsWith("/api/clinical/labs")) return "Labs";
        if (normalized.StartsWith("/api/clinical/vitals")) return "Vitals";
        if (normalized.StartsWith("/api/clinical/medications")) return "Medications";
        if (normalized.StartsWith("/api/clinical/allergies")) return "Allergies";
        if (normalized.StartsWith("/api/clinical/conditions")) return "Conditions";
        if (normalized.StartsWith("/api/clinical/procedures")) return "Procedures";
        if (normalized.StartsWith("/api/clinical/immunizations")) return "Immunizations";
        if (normalized.StartsWith("/api/clinical/summary/export")) return "Exports";
        if (normalized.StartsWith("/api/clinical/snapshot")) return "HealthSnapshot";

        if (normalized.StartsWith("/api/appointments")) return "Appointments";
        if (normalized.StartsWith("/api/messages"))
        {
            if (normalized.Contains("care-team")) return null;
            return "Messages";
        }

        if (normalized.StartsWith("/r4/"))
        {
            if (normalized == "/r4/metadata") return null;
            if (normalized.StartsWith("/r4/observation")) return "Labs";
            if (normalized.StartsWith("/r4/condition")) return "Conditions";
            if (normalized.StartsWith("/r4/allergyintolerance")) return "Allergies";
            if (normalized.StartsWith("/r4/medicationrequest")) return "Medications";
            if (normalized.StartsWith("/r4/appointment")) return "Appointments";
            if (normalized.StartsWith("/r4/patient"))
            {
                return normalized.Contains("$everything") ? "Exports" : "Patient";
            }
        }

        return null;
    }
}
