using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.DistributedLock;
using MedicalPortal.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Services;

public class AppointmentReminderWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AppointmentReminderWorker> _logger;
    private readonly IDistributedLockService? _lockService;

    public AppointmentReminderWorker(
        IServiceProvider serviceProvider,
        ILogger<AppointmentReminderWorker> logger,
        IDistributedLockService? lockService = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _lockService = lockService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AppointmentReminderWorker background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessUpcomingAppointmentRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing appointment reminders.");
            }

            // Run check every 60 seconds
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    public async Task<int> ProcessUpcomingAppointmentRemindersAsync(CancellationToken cancellationToken = default)
    {
        var lockService = _lockService ?? _serviceProvider.GetService<IDistributedLockService>();
        if (lockService != null)
        {
            await using var lockHandle = await lockService.TryAcquireLockAsync("AppointmentReminderWorker", cancellationToken: cancellationToken);
            if (!lockHandle.IsAcquired)
            {
                _logger.LogInformation("AppointmentReminderWorker skipped: cluster lock held by another instance.");
                return 0;
            }

            return await DoProcessUpcomingAppointmentRemindersAsync(cancellationToken);
        }

        return await DoProcessUpcomingAppointmentRemindersAsync(cancellationToken);
    }

    private async Task<int> DoProcessUpcomingAppointmentRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var deliveryService = scope.ServiceProvider.GetService<INotificationDeliveryService>();

        var now = DateTime.UtcNow;
        var upcomingWindow = now.AddHours(48);

        var upcomingAppointments = await db.Appointments
            .Include(a => a.Patient).ThenInclude(p => p.Person)
            .Include(a => a.Provider)
            .Where(a => a.StartAt >= now && a.StartAt <= upcomingWindow &&
                        (a.Status == AppointmentStatus.CONFIRMED || a.Status == AppointmentStatus.REQUESTED))
            .ToListAsync(cancellationToken);

        int remindersDispatched = 0;

        foreach (var appt in upcomingAppointments)
        {
            var apptIdStr = appt.Id.ToString();
            var alreadySent = await db.AuditLogs
                .AnyAsync(l => l.Action == "APPOINTMENT_REMINDER_DISPATCHED" && l.ResourceId == apptIdStr, cancellationToken);

            if (!alreadySent)
            {
                var providerName = appt.Provider != null ? $"Dr. {appt.Provider.FirstName} {appt.Provider.LastName}" : "Your Care Team";

                _logger.LogInformation(
                    "DISPATCHED REMINDER: Patient {PatientId} has upcoming appointment with {Provider} on {Start:yyyy-MM-dd HH:mm} UTC.",
                    appt.PatientId, providerName, appt.StartAt);

                // Dispatch real notification respecting preferences, opt-outs, and PHI hygiene
                if (deliveryService != null)
                {
                    try
                    {
                        await deliveryService.SendAppointmentReminderAsync(appt, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to dispatch notification for appointment {AppointmentId}", appt.Id);
                    }
                }

                db.AuditLogs.Add(new Core.Entities.AuditLog
                {
                    Action = "APPOINTMENT_REMINDER_DISPATCHED",
                    ResourceType = "Appointment",
                    ResourceId = apptIdStr,
                    PatientId = appt.PatientId,
                    MetadataJson = $"{{\"reason\":\"{appt.ReasonForVisit}\",\"details\":\"Automated reminder dispatched for appointment on {appt.StartAt:yyyy-MM-dd HH:mm} UTC with {providerName}\"}}"
                });

                remindersDispatched++;
            }
        }

        if (remindersDispatched > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return remindersDispatched;
    }
}
