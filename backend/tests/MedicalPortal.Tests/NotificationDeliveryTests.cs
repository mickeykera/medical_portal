using System.Net;
using System.Net.Http.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Notifications;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedicalPortal.Tests;

public class NotificationDeliveryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NotificationDeliveryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static (MedicalPortalDbContext Db, FakeNotificationSender FakeSender, NotificationDeliveryService Service) CreateTestHarness(
        int maxAttempts = 3,
        int initialBackoffMs = 1)
    {
        var dbName = $"notif_test_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new MedicalPortalDbContext(options);

        var fakeSender = new FakeNotificationSender();
        var configDict = new Dictionary<string, string?>
        {
            ["Notifications:PortalBaseUrl"] = "https://portal.addiscarehealth.org",
            ["Notifications:MaxRetryAttempts"] = maxAttempts.ToString(),
            ["Notifications:InitialBackoffMs"] = initialBackoffMs.ToString()
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
        var logger = NullLogger<NotificationDeliveryService>.Instance;

        var service = new NotificationDeliveryService(db, fakeSender, config, logger);
        return (db, fakeSender, service);
    }

    [Fact]
    public async Task SendAppointmentReminder_DefaultPreferences_DeliversBothEmailAndSms()
    {
        var (db, fakeSender, service) = CreateTestHarness();

        var person = new Person
        {
            FirstName = "Alice",
            LastName = "Morgan",
            Email = "alice.morgan@example.com",
            Phone = "+15551234567"
        };
        var patient = new Patient { Person = person, Mrn = "MRN-ALICE" };
        var provider = new Provider { FirstName = "Gregory", LastName = "House", Specialty = "Infectious Disease" };

        var appt = new Appointment
        {
            Patient = patient,
            Provider = provider,
            StartAt = DateTime.UtcNow.AddDays(1),
            EndAt = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            Status = AppointmentStatus.CONFIRMED,
            ReasonForVisit = "Follow-up consultation",
            PatientNotes = "Confidential clinical notes"
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        db.Providers.Add(provider);
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        var records = await service.SendAppointmentReminderAsync(appt);

        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(NotificationDeliveryStatus.DELIVERED, r.Status));
        Assert.Contains(records, r => r.Channel == NotificationChannel.EMAIL);
        Assert.Contains(records, r => r.Channel == NotificationChannel.SMS);

        Assert.Single(fakeSender.SentEmails);
        Assert.Single(fakeSender.SentSms);
    }

    [Fact]
    public async Task SendAppointmentReminder_MutedPreference_RecordsOptedOut_DoesNotCallSender()
    {
        var (db, fakeSender, service) = CreateTestHarness();

        var person = new Person
        {
            FirstName = "Bob",
            LastName = "Taylor",
            Email = "bob.taylor@example.com",
            Phone = "+15559876543"
        };
        var patient = new Patient { Person = person, Mrn = "MRN-BOB" };
        var provider = new Provider { FirstName = "Amara", LastName = "Patel" };

        var appt = new Appointment
        {
            Patient = patient,
            Provider = provider,
            StartAt = DateTime.UtcNow.AddDays(2),
            EndAt = DateTime.UtcNow.AddDays(2).AddMinutes(30),
            Status = AppointmentStatus.CONFIRMED
        };

        // Mute appointment notifications
        var pref = new NotificationPreference
        {
            PersonId = person.Id,
            Category = "Appointments",
            Muted = true
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        db.Providers.Add(provider);
        db.Appointments.Add(appt);
        db.NotificationPreferences.Add(pref);
        await db.SaveChangesAsync();

        var records = await service.SendAppointmentReminderAsync(appt);

        Assert.Single(records);
        Assert.Equal(NotificationDeliveryStatus.OPTED_OUT, records[0].Status);
        Assert.Contains("Muted", records[0].ErrorMessage);

        // Sender was never invoked
        Assert.Empty(fakeSender.SentEmails);
        Assert.Empty(fakeSender.SentSms);
    }

    [Fact]
    public async Task SendAppointmentReminder_DisabledChannels_RespectsChannelOptOuts()
    {
        var (db, fakeSender, service) = CreateTestHarness();

        var person = new Person
        {
            FirstName = "Charlie",
            LastName = "Kelly",
            Email = "charlie@example.com",
            Phone = "+15555550123"
        };
        var patient = new Patient { Person = person, Mrn = "MRN-CHARLIE" };
        var appt = new Appointment
        {
            Patient = patient,
            StartAt = DateTime.UtcNow.AddDays(1),
            EndAt = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            Status = AppointmentStatus.CONFIRMED
        };

        // Email disabled, SMS enabled
        var pref = new NotificationPreference
        {
            PersonId = person.Id,
            Category = "Appointments",
            EmailEnabled = false,
            SmsEnabled = true,
            Muted = false
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        db.Appointments.Add(appt);
        db.NotificationPreferences.Add(pref);
        await db.SaveChangesAsync();

        var records = await service.SendAppointmentReminderAsync(appt);

        Assert.Single(records);
        Assert.Equal(NotificationChannel.SMS, records[0].Channel);
        Assert.Equal(NotificationDeliveryStatus.DELIVERED, records[0].Status);

        Assert.Empty(fakeSender.SentEmails);
        Assert.Single(fakeSender.SentSms);
    }

    [Fact]
    public async Task HipaaHygiene_AppointmentReminder_NeverContainsClinicalDetails_LinksToPortal()
    {
        var (db, fakeSender, service) = CreateTestHarness();

        var person = new Person
        {
            FirstName = "Diana",
            LastName = "Prince",
            Email = "diana.prince@example.com",
            Phone = "+15554443322"
        };
        var patient = new Patient { Person = person, Mrn = "MRN-DIANA" };
        var provider = new Provider { FirstName = "Stephen", LastName = "Strange", Specialty = "Psychiatry" };

        var appt = new Appointment
        {
            Patient = patient,
            Provider = provider,
            StartAt = DateTime.UtcNow.AddDays(1),
            EndAt = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            Status = AppointmentStatus.CONFIRMED,
            ReasonForVisit = "Major depressive disorder evaluation",
            PatientNotes = "Patient expressed extreme anxiety"
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        db.Providers.Add(provider);
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        var records = await service.SendAppointmentReminderAsync(appt);

        Assert.Equal(2, records.Count);

        var email = fakeSender.SentEmails.Single();
        var sms = fakeSender.SentSms.Single();

        // 1. Must link to portal
        Assert.Contains("https://portal.addiscarehealth.org/appointments", email.BodyText);
        Assert.Contains("https://portal.addiscarehealth.org/appointments", sms.Message);

        // 2. Must NOT contain ReasonForVisit, Provider Specialty, or PatientNotes
        Assert.DoesNotContain("Major depressive disorder", email.BodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Psychiatry", email.BodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anxiety", email.BodyText, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("Major depressive disorder", sms.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Psychiatry", sms.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anxiety", sms.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetriesAndBackoff_TransientErrors_RetriesAndSucceeds()
    {
        var (db, fakeSender, service) = CreateTestHarness(maxAttempts: 3, initialBackoffMs: 1);

        var person = new Person
        {
            FirstName = "Evan",
            LastName = "Wright",
            Email = "evan.wright@example.com"
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        // Simulate 2 transient failures followed by success
        fakeSender.FailEmailAttemptsRemaining = 2;
        fakeSender.EmailFailIsTransient = true;
        fakeSender.SimulatedErrorMessage = "HTTP 429 Too Many Requests";

        var record = await service.DispatchEmailAsync(person, "Billing", "Invoice Reminder", "Please visit https://portal.addiscarehealth.org/billing");

        Assert.Equal(NotificationDeliveryStatus.DELIVERED, record.Status);
        Assert.Equal(3, record.AttemptCount);
        Assert.NotNull(record.DeliveredAtUtc);
        Assert.NotNull(record.ExternalMessageId);
        Assert.Null(record.ErrorMessage);

        // Verify record in database
        var savedRecord = await db.NotificationDeliveryRecords.FindAsync(record.Id);
        Assert.NotNull(savedRecord);
        Assert.Equal(NotificationDeliveryStatus.DELIVERED, savedRecord.Status);
        Assert.Equal(3, savedRecord.AttemptCount);
    }

    [Fact]
    public async Task RetriesAndBackoff_ExhaustedAttempts_RecordsFailureWithAuditTrail()
    {
        var (db, fakeSender, service) = CreateTestHarness(maxAttempts: 3, initialBackoffMs: 1);

        var person = new Person
        {
            FirstName = "Fiona",
            LastName = "Gallagher",
            Phone = "+251911778899"
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        // Simulate 3 transient failures (exhausting all 3 attempts)
        fakeSender.FailSmsAttemptsRemaining = 5;
        fakeSender.SmsFailIsTransient = true;
        fakeSender.SimulatedErrorMessage = "HTTP 503 Service Unavailable";

        var record = await service.DispatchSmsAsync(person, "Appointments", "Reminder: https://portal.addiscarehealth.org/appointments");

        Assert.Equal(NotificationDeliveryStatus.FAILED, record.Status);
        Assert.Equal(3, record.AttemptCount);
        Assert.Contains("503", record.ErrorMessage);
        Assert.Null(record.DeliveredAtUtc);

        var savedRecord = await db.NotificationDeliveryRecords.FindAsync(record.Id);
        Assert.NotNull(savedRecord);
        Assert.Equal(NotificationDeliveryStatus.FAILED, savedRecord.Status);
    }

    [Fact]
    public async Task RetriesAndBackoff_NonTransientError_DoesNotRetry()
    {
        var (db, fakeSender, service) = CreateTestHarness(maxAttempts: 3, initialBackoffMs: 1);

        var person = new Person
        {
            FirstName = "George",
            LastName = "Costanza",
            Email = "george@vandelay.example"
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        // Non-transient error (e.g. 400 Bad Request or invalid email)
        fakeSender.FailEmailAttemptsRemaining = 1;
        fakeSender.EmailFailIsTransient = false;
        fakeSender.SimulatedErrorMessage = "HTTP 400 Bad Request: Invalid Recipient";

        var record = await service.DispatchEmailAsync(person, "General", "Update", "https://portal.addiscarehealth.org");

        Assert.Equal(NotificationDeliveryStatus.FAILED, record.Status);
        Assert.Equal(1, record.AttemptCount);
        Assert.Contains("400 Bad Request", record.ErrorMessage);
    }

    [Fact]
    public async Task SendBillingNotice_FormatsAmountAndDueDate_LinksToBillingPortal()
    {
        var (db, fakeSender, service) = CreateTestHarness();

        var person = new Person
        {
            FirstName = "Hannah",
            LastName = "Abbott",
            Email = "hannah@example.com",
            Phone = "+251911332211"
        };
        var patientId = Guid.NewGuid();
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var dueAt = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        var records = await service.SendBillingNoticeAsync(person, patientId, 7500, dueAt, "inst_123");

        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(NotificationDeliveryStatus.DELIVERED, r.Status));

        var email = fakeSender.SentEmails.Single();
        var sms = fakeSender.SentSms.Single();

        // Check amount formatting (ETB 75.00) and portal link
        Assert.Contains("75.00", email.BodyText);
        Assert.Contains("ETB", email.BodyText);
        Assert.Contains("https://portal.addiscarehealth.org/billing", email.BodyText);

        Assert.Contains("75.00", sms.Message);
        Assert.Contains("ETB", sms.Message);
        Assert.Contains("https://portal.addiscarehealth.org/billing", sms.Message);
    }

    [Fact]
    public async Task AppointmentReminderWorker_IntegratesNotificationServiceAndAuditLog()
    {
        var dbName = $"worker_test_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MedicalPortalDbContext>(opt => opt.UseInMemoryDatabase(dbName));

        var fakeSender = new FakeNotificationSender();
        services.AddSingleton<INotificationSender>(fakeSender);

        var configDict = new Dictionary<string, string?>
        {
            ["Notifications:PortalBaseUrl"] = "https://portal.addiscarehealth.org",
            ["Notifications:MaxRetryAttempts"] = "3",
            ["Notifications:InitialBackoffMs"] = "1"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddScoped<INotificationDeliveryService, NotificationDeliveryService>();

        var sp = services.BuildServiceProvider();

        Guid apptId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var person = new Person
            {
                FirstName = "Ian",
                LastName = "Malcolm",
                Email = "ian.malcolm@example.com",
                Phone = "+15551112233"
            };
            var patient = new Patient { Person = person, Mrn = "MRN-IAN" };
            var provider = new Provider { FirstName = "Ellie", LastName = "Sattler", Specialty = "Paleobotany" };

            var appt = new Appointment
            {
                Patient = patient,
                Provider = provider,
                StartAt = DateTime.UtcNow.AddHours(20),
                EndAt = DateTime.UtcNow.AddHours(20.5),
                Status = AppointmentStatus.CONFIRMED,
                ReasonForVisit = "Follow-up visit"
            };

            db.Persons.Add(person);
            db.Patients.Add(patient);
            db.Providers.Add(provider);
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            apptId = appt.Id;
        }

        var worker = new AppointmentReminderWorker(sp, NullLogger<AppointmentReminderWorker>.Instance);
        var dispatched = await worker.ProcessUpcomingAppointmentRemindersAsync();

        Assert.Equal(1, dispatched);

        // Verify notification delivered
        Assert.Single(fakeSender.SentEmails);
        Assert.Single(fakeSender.SentSms);

        // Verify audit log and delivery records
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var auditLog = await db.AuditLogs.FirstOrDefaultAsync(l => l.Action == "APPOINTMENT_REMINDER_DISPATCHED" && l.ResourceId == apptId.ToString());
            Assert.NotNull(auditLog);

            var deliveryRecords = await db.NotificationDeliveryRecords
                .Where(r => r.ReferenceType == "Appointment" && r.ReferenceId == apptId.ToString())
                .ToListAsync();
            Assert.Equal(2, deliveryRecords.Count);
            Assert.All(deliveryRecords, r => Assert.Equal(NotificationDeliveryStatus.DELIVERED, r.Status));
        }

        // Running again is idempotent (no duplicate sends)
        var secondRun = await worker.ProcessUpcomingAppointmentRemindersAsync();
        Assert.Equal(0, secondRun);
        Assert.Single(fakeSender.SentEmails);
        Assert.Single(fakeSender.SentSms);
    }

    [Fact]
    public async Task BillingInstallmentWorker_IntegratesNotificationServiceAndAuditLog()
    {
        var dbName = $"billing_worker_test_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MedicalPortalDbContext>(opt => opt.UseInMemoryDatabase(dbName));

        var fakeSender = new FakeNotificationSender();
        services.AddSingleton<INotificationSender>(fakeSender);

        var configDict = new Dictionary<string, string?>
        {
            ["Notifications:PortalBaseUrl"] = "https://portal.addiscarehealth.org",
            ["Notifications:MaxRetryAttempts"] = "3",
            ["Notifications:InitialBackoffMs"] = "1"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddScoped<INotificationDeliveryService, NotificationDeliveryService>();

        var sp = services.BuildServiceProvider();

        Guid installmentId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var person = new Person
            {
                FirstName = "Julia",
                LastName = "Roberts",
                Email = "julia.roberts@example.com",
                Phone = "+15558889900"
            };
            var patient = new Patient { Person = person, Mrn = "MRN-JULIA" };
            var plan = new PaymentPlan
            {
                Patient = patient,
                TotalCents = 15000,
                NumberOfPayments = 3,
                Status = PaymentPlanStatus.ACTIVE
            };
            var installment = new PaymentInstallment
            {
                Plan = plan,
                Sequence = 1,
                AmountCents = 5000,
                DueAt = DateTime.UtcNow.AddDays(-2), // Overdue
                PaidAt = null
            };

            db.Persons.Add(person);
            db.Patients.Add(patient);
            db.PaymentPlans.Add(plan);
            db.PaymentInstallments.Add(installment);
            await db.SaveChangesAsync();
            installmentId = installment.Id;
        }

        var worker = new BillingInstallmentWorker(sp, NullLogger<BillingInstallmentWorker>.Instance);
        var updated = await worker.ProcessOverdueInstallmentsAsync();

        Assert.Equal(1, updated);

        // Verify billing notice dispatched
        Assert.Single(fakeSender.SentEmails);
        Assert.Single(fakeSender.SentSms);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var auditLog = await db.AuditLogs.FirstOrDefaultAsync(l => l.Action == "INSTALLMENT_OVERDUE" && l.ResourceId == installmentId.ToString());
            Assert.NotNull(auditLog);

            var deliveryRecords = await db.NotificationDeliveryRecords
                .Where(r => r.ReferenceType == "PaymentInstallment" && r.ReferenceId == installmentId.ToString())
                .ToListAsync();
            Assert.Equal(2, deliveryRecords.Count);
        }

        // Idempotent
        var secondRun = await worker.ProcessOverdueInstallmentsAsync();
        Assert.Equal(0, secondRun);
    }

    [Fact]
    public async Task BaaNotificationSender_ConfigurationRoutesToAppropriateSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var fakeSender = new FakeNotificationSender();
        services.AddSingleton(fakeSender);

        var configDict = new Dictionary<string, string?>
        {
            ["Notifications:EmailProvider"] = "Fake",
            ["Notifications:SmsProvider"] = "Fake"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddTransient<BaaNotificationSender>();

        var sp = services.BuildServiceProvider();
        var baaSender = sp.GetRequiredService<BaaNotificationSender>();

        var emailReq = new NotificationEmailRequest("test@example.com", "Test", "Message body");
        var emailResult = await baaSender.SendEmailAsync(emailReq);
        Assert.True(emailResult.Success);
        Assert.Single(fakeSender.SentEmails);

        var smsReq = new NotificationSmsRequest("+15551234567", "SMS body");
        var smsResult = await baaSender.SendSmsAsync(smsReq);
        Assert.True(smsResult.Success);
        Assert.Single(fakeSender.SentSms);
    }
}
