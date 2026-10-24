using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Hubs;

public class PortalHub : Hub
{
    private readonly ITokenService _tokenService;
    private readonly ISessionService _sessionService;
    private readonly IAccessControlService _accessControl;
    private readonly MedicalPortalDbContext _db;

    public PortalHub(
        ITokenService tokenService,
        ISessionService sessionService,
        IAccessControlService accessControl,
        MedicalPortalDbContext db)
    {
        _tokenService = tokenService;
        _sessionService = sessionService;
        _accessControl = accessControl;
        _db = db;
    }

    private async Task<AccessTokenClaims?> GetActorAsync()
    {
        if (Context.Items.TryGetValue("Actor", out var cached) && cached is AccessTokenClaims claims)
        {
            return claims;
        }

        var httpContext = Context.GetHttpContext();
        if (httpContext == null) return null;

        string? token = httpContext.Request.Cookies["mp_session"];
        if (string.IsNullOrEmpty(token))
        {
            var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader.Substring("Bearer ".Length).Trim();
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            token = httpContext.Request.Query["access_token"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(token)) return null;

        var validated = _tokenService.ValidateAccessToken(token);
        if (validated == null) return null;

        // Unify SignalR session validation with HTTP:
        // Enforce session presence, active account, idle timeout, role matching, and auth version
        var sessionResult = await _sessionService.ValidateSessionAsync(
            validated.Sid,
            validated.UserAccountId,
            validated.AuthorizationVersion,
            validated.Role);

        if (!sessionResult.IsValid || sessionResult.Account == null)
        {
            return null;
        }

        var authoritativeActor = new AccessTokenClaims(
            sessionResult.Account.Id,
            sessionResult.Account.PersonId,
            sessionResult.Account.Role,
            validated.Sid,
            sessionResult.Account.AuthorizationVersion);

        Context.Items["Actor"] = authoritativeActor;
        return authoritativeActor;
    }

    public override async Task OnConnectedAsync()
    {
        var actor = await GetActorAsync();
        if (actor == null)
        {
            Context.Abort();
            return;
        }

        // Register connection into user's private person group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{actor.PersonId.ToString().ToLower()}");
        await base.OnConnectedAsync();
    }

    public async Task JoinPatientRoom(string patientId)
    {
        var actor = await GetActorAsync();
        if (actor == null)
        {
            throw new HubException("Authentication required.");
        }

        if (string.IsNullOrWhiteSpace(patientId) || !Guid.TryParse(patientId, out var parsedPatientId))
        {
            throw new HubException("Valid patient ID is required.");
        }

        var decision = await _accessControl.ResolveAccessAsync(actor, parsedPatientId, Permission.viewRecords);
        if (!decision.Allowed)
        {
            throw new HubException("Access denied: You do not have permission to access this patient's records.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"patient_{parsedPatientId.ToString().ToLower()}");
    }

    public async Task LeavePatientRoom(string patientId)
    {
        var actor = await GetActorAsync();
        if (actor == null)
        {
            throw new HubException("Authentication required.");
        }

        if (!string.IsNullOrWhiteSpace(patientId) && Guid.TryParse(patientId, out var parsedPatientId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"patient_{parsedPatientId.ToString().ToLower()}");
        }
    }

    public async Task JoinClinicianRoom(string providerId)
    {
        var actor = await GetActorAsync();
        if (actor == null)
        {
            throw new HubException("Authentication required.");
        }

        if (string.IsNullOrWhiteSpace(providerId) || !Guid.TryParse(providerId, out var parsedProviderId))
        {
            throw new HubException("Valid provider ID is required.");
        }

        if (actor.Role != UserRole.CLINICIAN && actor.Role != UserRole.PHYSICIAN && actor.Role != UserRole.NURSE && actor.Role != UserRole.ADMIN && actor.Role != UserRole.SYSTEM_ADMIN)
        {
            throw new HubException("Access denied: Clinical role required.");
        }

        if (actor.Role is UserRole.CLINICIAN or UserRole.PHYSICIAN or UserRole.NURSE)
        {
            var isOwnProvider = await _db.Providers.AsNoTracking()
                .AnyAsync(pr => pr.Id == parsedProviderId && pr.AccountId == actor.UserAccountId);

            if (!isOwnProvider)
            {
                throw new HubException("Access denied: You are not authorized for this provider room.");
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"provider_{parsedProviderId.ToString().ToLower()}");
    }

    public async Task JoinComplianceQueue()
    {
        var actor = await GetActorAsync();
        if (actor == null)
        {
            throw new HubException("Authentication required.");
        }

        if (actor.Role != UserRole.ADMIN && actor.Role != UserRole.SYSTEM_ADMIN && actor.Role != UserRole.COMPLIANCE_AUDITOR)
        {
            throw new HubException("Access denied: Compliance administrator role required.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, "compliance_queue");
    }
}
