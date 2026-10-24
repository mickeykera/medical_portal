using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.DistributedLock;
using MedicalPortal.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Services;

public class BillingInstallmentWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BillingInstallmentWorker> _logger;
    private readonly IDistributedLockService? _lockService;

    public BillingInstallmentWorker(
        IServiceProvider serviceProvider,
        ILogger<BillingInstallmentWorker> logger,
        IDistributedLockService? lockService = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _lockService = lockService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BillingInstallmentWorker background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOverdueInstallmentsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing billing installments.");
            }

            // Run check every 60 seconds
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    public async Task<int> ProcessOverdueInstallmentsAsync(CancellationToken cancellationToken = default)
    {
        var lockService = _lockService ?? _serviceProvider.GetService<IDistributedLockService>();
        if (lockService != null)
        {
            await using var lockHandle = await lockService.TryAcquireLockAsync("BillingInstallmentWorker", cancellationToken: cancellationToken);
            if (!lockHandle.IsAcquired)
            {
                _logger.LogInformation("BillingInstallmentWorker skipped: cluster lock held by another instance.");
                return 0;
            }

            return await DoProcessOverdueInstallmentsAsync(cancellationToken);
        }

        return await DoProcessOverdueInstallmentsAsync(cancellationToken);
    }

    private async Task<int> DoProcessOverdueInstallmentsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var deliveryService = scope.ServiceProvider.GetService<INotificationDeliveryService>();

        var now = DateTime.UtcNow;

        // Find unpaid installments that have passed their due date
        var overdueInstallments = await db.PaymentInstallments
            .Include(pi => pi.Plan).ThenInclude(p => p.Patient).ThenInclude(pt => pt.Person)
            .Where(pi => pi.PaidAt == null && pi.DueAt < now)
            .ToListAsync(cancellationToken);

        int updated = 0;

        foreach (var installment in overdueInstallments)
        {
            var alreadyLogged = await db.AuditLogs.AnyAsync(l =>
                l.Action == "INSTALLMENT_OVERDUE" && l.ResourceId == installment.Id.ToString(), cancellationToken);

            if (!alreadyLogged)
            {
                if (deliveryService != null && installment.Plan?.Patient?.Person != null)
                {
                    try
                    {
                        await deliveryService.SendBillingNoticeAsync(
                            installment.Plan.Patient.Person,
                            installment.Plan.PatientId,
                            installment.AmountCents,
                            installment.DueAt,
                            installment.Id.ToString(),
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to dispatch billing notice for installment {InstallmentId}", installment.Id);
                    }
                }

                db.AuditLogs.Add(new Core.Entities.AuditLog
                {
                    Action = "INSTALLMENT_OVERDUE",
                    ResourceType = "PaymentInstallment",
                    ResourceId = installment.Id.ToString(),
                    PatientId = installment.Plan?.PatientId,
                    MetadataJson = $"{{\"sequence\":{installment.Sequence},\"amountCents\":{installment.AmountCents},\"dueAt\":\"{installment.DueAt:o}\"}}"
                });
                updated++;
            }
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Updated {Count} overdue billing installments.", updated);
        }

        return updated;
    }
}
