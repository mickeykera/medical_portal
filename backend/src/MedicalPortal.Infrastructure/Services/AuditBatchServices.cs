using System.Threading.Channels;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Services;

public interface IAuditBatchQueue
{
    ValueTask EnqueueAsync(AuditLog log, CancellationToken cancellationToken = default);
    bool TryEnqueue(AuditLog log);
    ChannelReader<AuditLog> Reader { get; }
    void NotifySaved(int count);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

public class AuditBatchQueue : IAuditBatchQueue
{
    private readonly Channel<AuditLog> _channel;
    private long _pendingCount;

    public AuditBatchQueue(int capacity = 10_000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<AuditLog>(options);
    }

    public ChannelReader<AuditLog> Reader => _channel.Reader;

    public async ValueTask EnqueueAsync(AuditLog log, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _pendingCount);
        await _channel.Writer.WriteAsync(log, cancellationToken);
    }

    public bool TryEnqueue(AuditLog log)
    {
        Interlocked.Increment(ref _pendingCount);
        if (_channel.Writer.TryWrite(log))
        {
            return true;
        }
        Interlocked.Decrement(ref _pendingCount);
        return false;
    }

    public void NotifySaved(int count)
    {
        Interlocked.Add(ref _pendingCount, -count);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        while (Volatile.Read(ref _pendingCount) > 0)
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}

public class AuditLogBatchWorker : BackgroundService
{
    private readonly IAuditBatchQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogBatchWorker> _logger;

    public AuditLogBatchWorker(
        IAuditBatchQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AuditLogBatchWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await _queue.Reader.WaitToReadAsync(stoppingToken))
                    break;

                var batch = new List<AuditLog>();
                while (batch.Count < 100 && _queue.Reader.TryRead(out var log))
                {
                    batch.Add(log);
                }

                if (batch.Count > 0)
                {
                    await SaveBatchAsync(batch, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing audit log batch.");
                await Task.Delay(50, stoppingToken);
            }
        }

        try
        {
            var remaining = new List<AuditLog>();
            while (_queue.Reader.TryRead(out var log))
            {
                remaining.Add(log);
            }
            if (remaining.Count > 0)
            {
                await SaveBatchAsync(remaining, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing remaining audit logs on shutdown.");
        }
    }

    private async Task SaveBatchAsync(List<AuditLog> batch, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        // Set default tenant context for background worker
        var tenantContext = scope.ServiceProvider.GetService<ITenantContext>();
        if (tenantContext != null && !tenantContext.IsResolved)
        {
            var defaultTenant = new Tenant { Id = TenantConstants.DefaultTenantId };
            tenantContext.SetTenant(defaultTenant);
        }
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        db.AuditLogs.AddRange(batch);
        await db.SaveChangesAsync(cancellationToken);
        _queue.NotifySaved(batch.Count);
    }
}
