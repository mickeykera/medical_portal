using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MedicalPortal.Web.Filters;

public class RequireAuthAttribute : TypeFilterAttribute
{
    public RequireAuthAttribute() : base(typeof(RequireAuthFilter)) { }
}

public class RequireAuthFilter : IAsyncActionFilter
{
    private readonly ITokenService _tokenService;
    private readonly ISessionService _sessionService;

    public RequireAuthFilter(ITokenService tokenService, ISessionService sessionService)
    {
        _tokenService = tokenService;
        _sessionService = sessionService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
        {
            await next();
            return;
        }

        string? token = null;
        var isCookieAuth = false;

        var authHeader = context.HttpContext.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader.Substring("Bearer ".Length).Trim();
        }
        else
        {
            token = context.HttpContext.Request.Cookies["mp_session"];
            if (!string.IsNullOrEmpty(token))
            {
                isCookieAuth = true;
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Authentication required." });
            return;
        }

        var claims = _tokenService.ValidateAccessToken(token);
        if (claims == null)
        {
            context.HttpContext.Response.Cookies.Delete("mp_session");
            context.Result = new UnauthorizedObjectResult(new { error = "Session expired. Please sign in again." });
            return;
        }

        // Server-side session verification by 'sid' (revocation, idle timeout, active account, role matching, auth version)
        var sessionResult = await _sessionService.ValidateSessionAsync(
            claims.Sid,
            claims.UserAccountId,
            claims.AuthorizationVersion,
            claims.Role);
        if (!sessionResult.IsValid)
        {
            if (isCookieAuth)
            {
                context.HttpContext.Response.Cookies.Delete("mp_session");
            }
            context.Result = new UnauthorizedObjectResult(new { error = sessionResult.FailureReason ?? "Session expired. Please sign in again." });
            return;
        }

        // Anti-CSRF protection: state-changing requests using cookie authentication require custom header
        if (isCookieAuth)
        {
            var method = context.HttpContext.Request.Method;
            var isStateChanging = HttpMethods.IsPost(method) ||
                                  HttpMethods.IsPut(method) ||
                                  HttpMethods.IsPatch(method) ||
                                  HttpMethods.IsDelete(method);

            if (isStateChanging)
            {
                var hasCsrfHeader = context.HttpContext.Request.Headers.ContainsKey("X-CSRF-Token") ||
                                    context.HttpContext.Request.Headers.ContainsKey("X-Requested-With");

                if (!hasCsrfHeader)
                {
                    context.Result = new ObjectResult(new
                    {
                        error = "CSRF verification failed: cookie-authenticated state-changing requests require a custom header (e.g. 'X-CSRF-Token' or 'X-Requested-With')."
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }
            }
        }

        // Reconcile claims against authoritative database record (prevents claim-forgery privilege escalation)
        var authoritativeActor = new AccessTokenClaims(
            sessionResult.Account!.Id,
            sessionResult.Account.PersonId,
            sessionResult.Account.Role,
            claims.Sid,
            sessionResult.Account.AuthorizationVersion);

        context.HttpContext.Items["Actor"] = authoritativeActor;
        if (sessionResult.Session != null)
        {
            context.HttpContext.Items["Session"] = sessionResult.Session;
        }
        await next();
    }
}

public class RequirePatientAccessAttribute : TypeFilterAttribute
{
    public RequirePatientAccessAttribute(Permission permission) : base(typeof(RequirePatientAccessFilter))
    {
        Arguments = new object[] { permission, AppResource.ClinicalNotes, AppAction.Read };
    }

    public RequirePatientAccessAttribute(Permission permission, AppResource resource, AppAction action = AppAction.Read) : base(typeof(RequirePatientAccessFilter))
    {
        Arguments = new object[] { permission, resource, action };
    }
}

public class RequirePatientAccessFilter : IAsyncActionFilter
{
    private readonly Permission _permission;
    private readonly AppResource _resource;
    private readonly AppAction _action;
    private readonly IAccessControlService _accessControl;

    public RequirePatientAccessFilter(
        Permission permission,
        AppResource resource,
        AppAction action,
        IAccessControlService accessControl)
    {
        _permission = permission;
        _resource = resource;
        _action = action;
        _accessControl = accessControl;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Items["Actor"] is not AccessTokenClaims actor)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Authentication required." });
            return;
        }

        var patientIdHeader = context.HttpContext.Request.Headers["x-patient-id"].FirstOrDefault();
        if (string.IsNullOrEmpty(patientIdHeader) || !Guid.TryParse(patientIdHeader, out var patientId))
        {
            context.Result = new BadRequestObjectResult(new { error = "x-patient-id header is required." });
            return;
        }

        Guid? departmentId = null;
        var deptHeader = context.HttpContext.Request.Headers["x-department-id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(deptHeader) && Guid.TryParse(deptHeader, out var parsedDeptId))
        {
            departmentId = parsedDeptId;
        }

        var decision = await _accessControl.ResolveAccessAsync(actor, patientId, _permission, _resource, _action, departmentId);
        if (!decision.Allowed)
        {
            context.Result = new ObjectResult(new
            {
                error = decision.Reason switch
                {
                    "department_scope_mismatch" => "Access denied: you do not have permission in this facility or department scope.",
                    "department_scope_required" => "Access denied: operational staff must supply an explicit x-department-id scope.",
                    "unassigned_staff_scope" => "Access denied: your staff account does not have an active department or facility assignment.",
                    _ => "You do not have permission to perform this action."
                },
                reason = decision.Reason
            })
            { StatusCode = 403 };
            return;
        }

        context.HttpContext.Items["TargetPatientId"] = patientId;
        context.HttpContext.Items["IsProxy"] = decision.IsProxy;
        if (decision.IsProxy && decision.PatientPersonId.HasValue)
        {
            context.HttpContext.Items["ActingForPersonId"] = decision.PatientPersonId.Value;
        }

        if (decision.IsBreakGlass)
        {
            context.HttpContext.Items["IsBreakGlass"] = true;
            context.HttpContext.Response.Headers["X-Break-Glass-Session"] = "true";
            if (decision.BreakGlassExpiresAt.HasValue)
            {
                context.HttpContext.Response.Headers["X-Break-Glass-Expires"] = decision.BreakGlassExpiresAt.Value.ToString("o");
            }
        }

        await next();
    }
}

public class RequireAdminAttribute : TypeFilterAttribute
{
    public RequireAdminAttribute() : base(typeof(RequireAdminFilter)) { }
}

public class RequireAdminFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Items["Actor"] is not AccessTokenClaims actor)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Authentication required." });
            return;
        }

        var isAdminOrAuditor = actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.COMPLIANCE_AUDITOR or UserRole.DEPT_ADMIN or UserRole.PLATFORM_ADMIN;
        if (!isAdminOrAuditor)
        {
            context.Result = new ObjectResult(new { error = "This action is restricted to compliance administrators." })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}

public class RequireClinicianAttribute : TypeFilterAttribute
{
    public RequireClinicianAttribute() : base(typeof(RequireClinicianFilter))
    {
        Arguments = new object[] { AppResource.ClinicalResults, AppAction.Write };
    }

    public RequireClinicianAttribute(AppResource resource, AppAction action) : base(typeof(RequireClinicianFilter))
    {
        Arguments = new object[] { resource, action };
    }
}

public class RequireClinicianFilter : IAsyncActionFilter
{
    private readonly AppResource _resource;
    private readonly AppAction _action;
    private readonly IAccessControlService _accessControl;

    public RequireClinicianFilter(AppResource resource, AppAction action, IAccessControlService accessControl)
    {
        _resource = resource;
        _action = action;
        _accessControl = accessControl;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Items["Actor"] is not AccessTokenClaims actor)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Authentication required." });
            return;
        }

        var isClinical = actor.Role is UserRole.CLINICIAN or UserRole.PHYSICIAN or UserRole.NURSE or UserRole.ADMIN or UserRole.SYSTEM_ADMIN;
        if (!isClinical)
        {
            context.Result = new ObjectResult(new { error = "This action is restricted to clinical staff." })
            { StatusCode = 403 };
            return;
        }

        var patientIdHeader = context.HttpContext.Request.Headers["x-patient-id"].FirstOrDefault();
        if (string.IsNullOrEmpty(patientIdHeader) || !Guid.TryParse(patientIdHeader, out var patientId))
        {
            context.Result = new BadRequestObjectResult(new { error = "x-patient-id header is required." });
            return;
        }

        Guid? departmentId = null;
        var deptHeader = context.HttpContext.Request.Headers["x-department-id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(deptHeader) && Guid.TryParse(deptHeader, out var parsedDeptId))
        {
            departmentId = parsedDeptId;
        }

        var decision = await _accessControl.ResolveAccessAsync(actor, patientId, Permission.viewRecords, _resource, _action, departmentId);
        if (!decision.Allowed)
        {
            context.Result = new ObjectResult(new
            {
                error = decision.Reason == "department_scope_mismatch"
                    ? "Access denied: you do not have permission in this facility or department scope."
                    : "You are not authorized for this clinical action.",
                reason = decision.Reason
            })
            { StatusCode = 403 };
            return;
        }

        context.HttpContext.Items["TargetPatientId"] = patientId;
        if (decision.IsBreakGlass)
        {
            context.HttpContext.Items["IsBreakGlass"] = true;
            context.HttpContext.Response.Headers["X-Break-Glass-Session"] = "true";
            if (decision.BreakGlassExpiresAt.HasValue)
            {
                context.HttpContext.Response.Headers["X-Break-Glass-Expires"] = decision.BreakGlassExpiresAt.Value.ToString("o");
            }
        }
        await next();
    }
}

public class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(AppResource resource, AppAction action) : base(typeof(RequirePermissionFilter))
    {
        Arguments = new object[] { resource, action };
    }
}

public class RequirePermissionFilter : IAsyncActionFilter
{
    private readonly AppResource _resource;
    private readonly AppAction _action;
    private readonly IAccessControlService _accessControl;

    public RequirePermissionFilter(AppResource resource, AppAction action, IAccessControlService accessControl)
    {
        _resource = resource;
        _action = action;
        _accessControl = accessControl;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Items["Actor"] is not AccessTokenClaims actor)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Authentication required." });
            return;
        }

        if (!PermissionMatrix.HasPermission(actor.Role, _resource, _action))
        {
            context.Result = new ObjectResult(new
            {
                error = $"Access denied: role '{actor.Role}' does not have permission to '{_action}' on '{_resource}'.",
                reason = "role_permission_denied"
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        var deptHeader = context.HttpContext.Request.Headers["x-department-id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(deptHeader) && Guid.TryParse(deptHeader, out var deptId))
        {
            if (actor.Role != UserRole.ADMIN && actor.Role != UserRole.SYSTEM_ADMIN && actor.Role != UserRole.PATIENT)
            {
                var inScope = await _accessControl.HasDepartmentScopeAsync(actor.UserAccountId, deptId);
                if (!inScope)
                {
                    context.Result = new ObjectResult(new
                    {
                        error = "Access denied: you do not have membership in this department scope.",
                        reason = "department_scope_mismatch"
                    })
                    { StatusCode = StatusCodes.Status403Forbidden };
                    return;
                }
            }
        }

        await next();
    }
}
