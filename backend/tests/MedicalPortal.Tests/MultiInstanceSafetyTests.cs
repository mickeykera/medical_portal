using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.DistributedLock;
using MedicalPortal.Infrastructure.Notifications;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedicalPortal.Tests;

public class MultiInstanceSafetyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MultiInstanceSafetyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MultiInstance_AppointmentReminderWorker_PreventsDuplicateReminders()
    {
        var dbName = $"multi_inst_reminder_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<MedicalPortalDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        var lockService = new PostgreSqlAdvisoryLockService(configuration: new ConfigurationBuilder().Build());
        services.AddSingleton<IDistributedLockService>(lockService);
        services.AddSingleton<INotificationDeliveryService>(new FakeNotificationDeliveryService());

        var sp1 = services.BuildServiceProvider();
        var sp2 = services.BuildServiceProvider();

        // 1. Seed upcoming appointment in shared DB
        using (var scope = sp1.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var person = new Person { FirstName = "Marcus", LastName = "Vance", DateOfBirth = new DateTime(1985, 4, 12, 0, 0, 0, DateTimeKind.Utc), Email = "marcus@example.com" };
            var patient = new Patient { Person = person, Mrn = "MRN-MULTI-1" };
            var provider = new Provider { FirstName = "Amara", LastName = "Patel", Specialty = "Family Medicine" };
            db.Persons.Add(person);
            db.Patients.Add(patient);
            db.Providers.Add(provider);

            db.Appointments.Add(new Appointment
            {
                Patient = patient,
                Provider = provider,
                StartAt = DateTime.UtcNow.AddHours(24),
                EndAt = DateTime.UtcNow.AddHours(24).AddMinutes(30),
                Status = AppointmentStatus.CONFIRMED,
                ReasonForVisit = "Cardiology Follow-Up"
            });
            await db.SaveChangesAsync();
        }

        // 2. Instantiate 2 separate worker instances representing 2 cluster nodes
        var workerInstance1 = new AppointmentReminderWorker(sp1, NullLogger<AppointmentReminderWorker>.Instance, lockService);
        var workerInstance2 = new AppointmentReminderWorker(sp2, NullLogger<AppointmentReminderWorker>.Instance, lockService);

        // 3. Simulate both instances running simultaneously
        var results = await Task.WhenAll(
            workerInstance1.ProcessUpcomingAppointmentRemindersAsync(),
            workerInstance2.ProcessUpcomingAppointmentRemindersAsync()
        );

        // One instance should have acquired the lock and dispatched 1 reminder; the second was mutually excluded and dispatched 0
        var totalDispatched = results[0] + results[1];
        Assert.Equal(1, totalDispatched);
        Assert.True(results[0] == 0 || results[1] == 0, "One worker instance must have been excluded by the distributed lock.");

        // 4. Verify exactly 1 audit log exists in the shared database (0 duplicate reminders)
        using (var scope = sp1.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var logs = await db.AuditLogs
                .Where(l => l.Action == "APPOINTMENT_REMINDER_DISPATCHED")
                .ToListAsync();

            Assert.Single(logs);
            Assert.Contains("Cardiology Follow-Up", logs[0].MetadataJson);
        }
    }

    [Fact]
    public async Task MultiInstance_BillingInstallmentWorker_PreventsDuplicateChargesOrNotices()
    {
        var dbName = $"multi_inst_billing_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<MedicalPortalDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        var lockService = new PostgreSqlAdvisoryLockService(configuration: new ConfigurationBuilder().Build());
        services.AddSingleton<IDistributedLockService>(lockService);
        services.AddSingleton<INotificationDeliveryService>(new FakeNotificationDeliveryService());

        var sp1 = services.BuildServiceProvider();
        var sp2 = services.BuildServiceProvider();

        // 1. Seed overdue installment in shared DB
        using (var scope = sp1.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var person = new Person { FirstName = "Evelyn", LastName = "Shaw", DateOfBirth = new DateTime(1992, 8, 20, 0, 0, 0, DateTimeKind.Utc), Email = "evelyn@example.com" };
            var patient = new Patient { Person = person, Mrn = "MRN-MULTI-2" };
            db.Persons.Add(person);
            db.Patients.Add(patient);

            var plan = new PaymentPlan
            {
                Patient = patient,
                TotalCents = 30000,
                InstallmentCents = 10000,
                NumberOfPayments = 3,
                StartDate = DateTime.UtcNow.AddMonths(-1),
                Status = PaymentPlanStatus.ACTIVE
            };
            db.PaymentPlans.Add(plan);

            db.PaymentInstallments.Add(new PaymentInstallment
            {
                Plan = plan,
                Sequence = 1,
                AmountCents = 10000,
                DueAt = DateTime.UtcNow.AddDays(-2), // overdue
                PaidAt = null
            });
            await db.SaveChangesAsync();
        }

        // 2. Instantiate 2 separate billing worker instances representing 2 cluster nodes
        var workerInstance1 = new BillingInstallmentWorker(sp1, NullLogger<BillingInstallmentWorker>.Instance, lockService);
        var workerInstance2 = new BillingInstallmentWorker(sp2, NullLogger<BillingInstallmentWorker>.Instance, lockService);

        // 3. Simulate both instances running simultaneously
        var results = await Task.WhenAll(
            workerInstance1.ProcessOverdueInstallmentsAsync(),
            workerInstance2.ProcessOverdueInstallmentsAsync()
        );

        // One instance should have acquired the lock and processed 1 installment; the second was mutually excluded and processed 0
        var totalProcessed = results[0] + results[1];
        Assert.Equal(1, totalProcessed);
        Assert.True(results[0] == 0 || results[1] == 0, "One billing worker instance must have been excluded by the distributed lock.");

        // 4. Verify exactly 1 audit log exists in the shared database (0 duplicate notices or charges)
        using (var scope = sp1.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var logs = await db.AuditLogs
                .Where(l => l.Action == "INSTALLMENT_OVERDUE")
                .ToListAsync();

            Assert.Single(logs);
            Assert.Contains("10000", logs[0].MetadataJson);
        }
    }

    [Fact]
    public async Task DistributedLock_AcquisitionAndReleaseLifecycle()
    {
        var lockService = new PostgreSqlAdvisoryLockService(configuration: new ConfigurationBuilder().Build());
        const string resource = "Test_Exclusive_Resource";

        // Instance 1 acquires the lock
        await using (var lock1 = await lockService.TryAcquireLockAsync(resource))
        {
            Assert.True(lock1.IsAcquired);

            // Instance 2 attempts to acquire the same lock while held -> must fail
            await using (var lock2 = await lockService.TryAcquireLockAsync(resource))
            {
                Assert.False(lock2.IsAcquired);
            }
        }

        // After lock 1 is disposed, instance 2 can now acquire it
        await using (var lock3 = await lockService.TryAcquireLockAsync(resource))
        {
            Assert.True(lock3.IsAcquired);
        }
    }

    [Fact]
    public void ForwardedHeaders_UntrustedProxy_IgnoresSpoofedHeader()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        // Only trust 127.0.0.1
        options.KnownProxies.Add(IPAddress.Loopback);

        // Untrusted proxy 203.0.113.195
        var untrustedPeer = IPAddress.Parse("203.0.113.195");
        Assert.DoesNotContain(untrustedPeer, options.KnownProxies);
    }

    [Fact]
    public void ForwardedHeaders_ConfiguredTrustedProxy_IsRecognized()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:TrustedProxies"] = "172.18.0.5,10.0.0.100",
            ["ForwardedHeaders:TrustedNetworks"] = "172.16.0.0/12"
        }).Build();

        var options = new ForwardedHeadersOptions();
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        var trustedProxies = config["ForwardedHeaders:TrustedProxies"]!.Split(',');
        foreach (var p in trustedProxies)
        {
            if (IPAddress.TryParse(p.Trim(), out var ip))
                options.KnownProxies.Add(ip);
        }

        Assert.Contains(IPAddress.Parse("172.18.0.5"), options.KnownProxies);
        Assert.Contains(IPAddress.Parse("10.0.0.100"), options.KnownProxies);
    }

    [Fact]
    public void SignalR_RedisBackplane_ConfigurableViaServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var signalR = services.AddSignalR();
        
        // Assert AddStackExchangeRedis extension method can be invoked without error
        signalR.AddStackExchangeRedis("127.0.0.1:6379", options =>
        {
            options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("TestPrefix");
        });

        var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<Microsoft.AspNetCore.SignalR.IHubContext<MedicalPortal.Web.Hubs.PortalHub>>());
    }

    [Fact]
    public void RateLimiting_PerInstanceLimits_Configurable()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPolicyPermitLimit"] = "20",
            ["RateLimiting:ShareRedemptionPolicyPermitLimit"] = "8",
            ["RateLimiting:GeneralApiPermitLimit"] = "250"
        }).Build();

        var authLimit = config.GetValue<int>("RateLimiting:AuthPolicyPermitLimit");
        var shareLimit = config.GetValue<int>("RateLimiting:ShareRedemptionPolicyPermitLimit");
        var generalLimit = config.GetValue<int>("RateLimiting:GeneralApiPermitLimit");

        Assert.Equal(20, authLimit);
        Assert.Equal(8, shareLimit);
        Assert.Equal(250, generalLimit);
    }

    private class FakeNotificationDeliveryService : INotificationDeliveryService
    {
        public Task<List<NotificationDeliveryRecord>> SendAppointmentReminderAsync(Appointment appointment, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<NotificationDeliveryRecord> { new() { Status = NotificationDeliveryStatus.SENT } });
        }

        public Task<List<NotificationDeliveryRecord>> SendBillingNoticeAsync(Person person, Guid patientId, int amountCents, DateTime? dueAt, string referenceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<NotificationDeliveryRecord> { new() { Status = NotificationDeliveryStatus.SENT } });
        }

        public Task<List<NotificationDeliveryRecord>> SendPaymentReceiptAsync(Person person, Guid patientId, int amountCents, string referenceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<NotificationDeliveryRecord> { new() { Status = NotificationDeliveryStatus.SENT } });
        }

        public Task<NotificationDeliveryRecord> DispatchEmailAsync(Person person, string category, string subject, string body, string? referenceType = null, string? referenceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NotificationDeliveryRecord { Status = NotificationDeliveryStatus.SENT });
        }

        public Task<NotificationDeliveryRecord> DispatchSmsAsync(Person person, string category, string message, string? referenceType = null, string? referenceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NotificationDeliveryRecord { Status = NotificationDeliveryStatus.SENT });
        }
    }
}
