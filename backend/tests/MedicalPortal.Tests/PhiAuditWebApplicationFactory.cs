using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MedicalPortal.Tests;

/// <summary>
/// WebApplicationFactory for PhiAudit tests that uses synchronous audit processing.
/// </summary>
public class PhiAuditWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the background worker that depends on IAuditBatchQueue
            var workerDescriptor = services.SingleOrDefault(d => d.ImplementationType == typeof(AuditLogBatchWorker));
            if (workerDescriptor != null)
            {
                services.Remove(workerDescriptor);
            }

            // Replace the audit batch queue with synchronous implementation
            var queueDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAuditBatchQueue));
            if (queueDescriptor != null)
            {
                services.Remove(queueDescriptor);
            }
            services.AddScoped<IAuditBatchQueue, SynchronousAuditQueue>();
        });
    }
}
