using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Offline;

/// <summary>
/// Background worker running on the facility server to drain the transactional outbox queue
/// to regional/cloud aggregators over HTTPS with store-and-forward semantics and backoff.
/// </summary>
public class OutboxSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<FacilityNodeOptions> _options;
    private readonly ILogger<OutboxSyncWorker> _logger;

    public OutboxSyncWorker(
        IServiceProvider serviceProvider,
        IOptions<FacilityNodeOptions> options,
        ILogger<OutboxSyncWorker>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger ?? NullLogger<OutboxSyncWorker>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxSyncWorker started for facility node {FacilityCode}.", _options.Value.FacilityCode);

        // Initial delay after process start to allow services to warm up
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxSyncDispatcher>();
                var referenceService = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();

                // 1. Drain pending outbox queue
                var report = await dispatcher.DispatchPendingMessagesAsync(stoppingToken);
                if (report.Processed > 0)
                {
                    _logger.LogInformation(
                        "Outbox dispatch cycle completed: {Processed} processed, {Delivered} delivered, {Failed} failed, {Dead} dead-lettered, {Remaining} queue depth.",
                        report.Processed, report.Delivered, report.Failed, report.DeadLettered, report.RemainingQueueDepth);
                }

                // 2. If online and queue is drained, check for updated reference packages
                if (report.LinkAvailable && report.RemainingQueueDepth == 0)
                {
                    await referenceService.PullAndApplyOnlinePackagesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OutboxSyncWorker encountered an unhandled exception during dispatch cycle. Waiting for next interval.");
            }

            var interval = Math.Max(5, _options.Value.SyncIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        }

        _logger.LogInformation("OutboxSyncWorker stopping for facility node {FacilityCode}.", _options.Value.FacilityCode);
    }
}
