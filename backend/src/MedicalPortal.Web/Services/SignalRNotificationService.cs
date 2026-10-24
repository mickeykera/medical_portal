using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MedicalPortal.Web.Services;

public class SignalRNotificationService : IPortalNotificationService
{
    private readonly IHubContext<PortalHub> _hubContext;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(IHubContext<PortalHub> hubContext, ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyAppointmentChangedAsync(Guid patientId, Guid? providerId, string action, object payload)
    {
        try
        {
            var data = new { action, patientId, providerId, payload, timestamp = DateTime.UtcNow };
            await _hubContext.Clients.Group($"patient_{patientId.ToString().ToLower()}").SendAsync("AppointmentUpdated", data);

            if (providerId.HasValue)
            {
                await _hubContext.Clients.Group($"provider_{providerId.Value.ToString().ToLower()}").SendAsync("AppointmentUpdated", data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR notification for appointment {Action}", action);
        }
    }

    public async Task NotifyNewMessageAsync(Guid threadId, Guid recipientPersonId, object payload, Guid? patientId = null)
    {
        try
        {
            var data = new { threadId, recipientPersonId, payload, timestamp = DateTime.UtcNow };

            // Send to recipient's private user group
            await _hubContext.Clients.Group($"user_{recipientPersonId.ToString().ToLower()}").SendAsync("NewMessage", data);

            // Send to the patient room group if specified, or if recipientPersonId represents the room
            if (patientId.HasValue)
            {
                await _hubContext.Clients.Group($"patient_{patientId.Value.ToString().ToLower()}").SendAsync("NewMessage", data);
            }
            else
            {
                await _hubContext.Clients.Group($"patient_{recipientPersonId.ToString().ToLower()}").SendAsync("NewMessage", data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR notification for message in thread {ThreadId}", threadId);
        }
    }

    public async Task NotifyRefillChangedAsync(Guid patientId, Guid refillId, string status, object payload)
    {
        try
        {
            var data = new { refillId, status, payload, timestamp = DateTime.UtcNow };
            await _hubContext.Clients.Group($"patient_{patientId.ToString().ToLower()}").SendAsync("RefillUpdated", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR notification for refill {RefillId}", refillId);
        }
    }

    public async Task NotifyBreakGlassAsync(Guid patientId, Guid clinicianId, string reason, DateTime expiresAt, object payload)
    {
        try
        {
            var data = new
            {
                type = "BREAK_GLASS_OVERRIDE",
                patientId,
                clinicianId,
                reason,
                expiresAt = expiresAt.ToString("o"),
                payload,
                timestamp = DateTime.UtcNow
            };

            // 1. Notify Patient Room / Group
            await _hubContext.Clients.Group($"patient_{patientId.ToString().ToLower()}").SendAsync("BreakGlassAlert", data);

            // 2. Notify Compliance Queue Room
            await _hubContext.Clients.Group("compliance_queue").SendAsync("ComplianceAlert", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR notification for break-glass override on patient {PatientId}", patientId);
        }
    }

    public async Task NotifyComplianceQueueAsync(string eventType, object payload)
    {
        try
        {
            var data = new
            {
                eventType,
                payload,
                timestamp = DateTime.UtcNow
            };
            await _hubContext.Clients.Group("compliance_queue").SendAsync("ComplianceAlert", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR compliance notification for {EventType}", eventType);
        }
    }
}
