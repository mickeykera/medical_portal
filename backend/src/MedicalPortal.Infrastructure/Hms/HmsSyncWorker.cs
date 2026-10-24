using MedicalPortal.Infrastructure.DistributedLock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Hms;

public class HmsSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HmsSyncWorker> _logger;
    private readonly IDistributedLockService? _lockService;

    public HmsSyncWorker(
        IServiceProvider serviceProvider,
        ILogger<HmsSyncWorker>? logger = null,
        IDistributedLockService? lockService = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? NullLogger<HmsSyncWorker>.Instance;
        _lockService = lockService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HmsSyncWorker background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessSyncCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during HMS sync cycle.");
            }

            // Sync every 60 seconds
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    public async Task<int> ProcessSyncCycleAsync(CancellationToken cancellationToken = default)
    {
        var lockService = _lockService ?? _serviceProvider.GetService<IDistributedLockService>();
        if (lockService != null)
        {
            await using var lockHandle = await lockService.TryAcquireLockAsync("HmsSyncWorker", cancellationToken: cancellationToken);
            if (!lockHandle.IsAcquired)
            {
                _logger.LogInformation("HmsSyncWorker skipped: cluster lock held by another instance.");
                return 0;
            }

            return await DoProcessSyncCycleAsync(cancellationToken);
        }

        return await DoProcessSyncCycleAsync(cancellationToken);
    }

    private async Task<int> DoProcessSyncCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IHmsSyncService>();
        var deadLetterService = scope.ServiceProvider.GetRequiredService<IHmsDeadLetterService>();

        int totalActions = 0;

        // 1. Push outbound unsynced appointments
        totalActions += await syncService.SyncOutboundAppointmentsAsync(cancellationToken);

        // 2. Push outbound unsynced refills
        totalActions += await syncService.SyncOutboundRefillsAsync(cancellationToken);

        // 3. Inbound pull sync for stale patients
        totalActions += await syncService.SyncStalePatientsAsync(cancellationToken: cancellationToken);

        // 4. Purge expired dead letters
        await deadLetterService.PurgeExpiredRecordsAsync(cancellationToken: cancellationToken);

        return totalActions;
    }
}
