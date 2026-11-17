using System.Threading.Channels;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Tests;

/// <summary>
/// Synchronous audit queue for tests - processes logs immediately without background worker.
/// </summary>
public class SynchronousAuditQueue : IAuditBatchQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SynchronousAuditQueue> _logger;

    public SynchronousAuditQueue(IServiceScopeFactory scopeFactory, ILogger<SynchronousAuditQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public ChannelReader<AuditLog> Reader => Channel.CreateUnbounded<AuditLog>().Reader;

    public ValueTask EnqueueAsync(AuditLog log, CancellationToken cancellationToken = default)
    {
        // Process immediately
        return ProcessLogAsync(log, cancellationToken);
    }

    public bool TryEnqueue(AuditLog log)
    {
        // Process immediately (fire and forget)
        _ = ProcessLogAsync(log, CancellationToken.None);
        return true;
    }

    public void NotifySaved(int count) { }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private async ValueTask ProcessLogAsync(AuditLog log, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var tenantContext = scope.ServiceProvider.GetService<MedicalPortal.Infrastructure.Tenancy.ITenantContext>();
        if (tenantContext != null && !tenantContext.IsResolved)
        {
            var defaultTenant = new MedicalPortal.Core.Entities.Tenant { Id = MedicalPortal.Core.Entities.TenantConstants.DefaultTenantId };
            tenantContext.SetTenant(defaultTenant);
        }
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
    }
}
