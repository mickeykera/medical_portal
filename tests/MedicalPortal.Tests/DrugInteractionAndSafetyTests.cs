using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedicalPortal.Tests;

public class DrugInteractionAndSafetyTests
{
    [Fact]
    public void DrugInteractionLogic_DetectsAnticoagulantAndNsaidHighRisk()
    {
        var existing = new[] { "Warfarin Sodium 5 MG Oral Tablet" };
        var proposed = "Ibuprofen 600 MG";

        var warnings = DrugInteractionLogic.CheckDrugInteractions(existing, proposed);

        Assert.NotEmpty(warnings);
        var w = warnings.First();
        Assert.Equal(InteractionSeverity.HIGH, w.Severity);
        Assert.Contains("bleeding", w.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("acetaminophen", w.ClinicalGuidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrugInteractionLogic_DetectsAceInhibitorAndPotassiumSparingHighRisk()
    {
        var existing = new[] { "Lisinopril 10 MG Oral Tablet" };
        var proposed = "Spironolactone 25 MG Oral Tablet";

        var warnings = DrugInteractionLogic.CheckDrugInteractions(existing, proposed);

        Assert.NotEmpty(warnings);
        var w = warnings.First();
        Assert.Equal(InteractionSeverity.HIGH, w.Severity);
        Assert.Contains("hyperkalemia", w.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrugInteractionLogic_DetectsSerotoninSyndromeRisk()
    {
        var existing = new[] { "Sertraline Hydrochloride 50 MG" };
        var proposed = "Tramadol Hydrochloride 50 MG";

        var warnings = DrugInteractionLogic.CheckDrugInteractions(existing, proposed);

        Assert.NotEmpty(warnings);
        Assert.Equal(InteractionSeverity.HIGH, warnings.First().Severity);
        Assert.Contains("Serotonin Syndrome", warnings.First().Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrugInteractionLogic_AllowsSafeCombinations()
    {
        var existing = new[] { "Metformin 500 MG", "Lisinopril 10 MG" };
        var proposed = "Amoxicillin 500 MG";

        var warnings = DrugInteractionLogic.CheckDrugInteractions(existing, proposed);

        Assert.Empty(warnings);
    }

    [Fact]
    public void ClinicalSummaryGenerator_ProducesFormattedHtmlWithDemographics()
    {
        var patient = new Patient
        {
            Mrn = "MRN-TEST-1234",
            Person = new Person
            {
                FirstName = "Jane",
                LastName = "Doe",
                DateOfBirth = new DateTime(1985, 5, 20, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        var allergies = new List<Allergy>
        {
            new() { Substance = "Penicillin", DrugClass = "Beta-lactam (penicillin)", DrugClassCode = "J01CA", Severity = AllergySeverity.SEVERE, Reaction = "Anaphylaxis" }
        };

        var meds = new List<Prescription>
        {
            new() { Dosage = "10mg daily", Frequency = "Once daily", Instructions = "Take in morning", Medication = new Medication { Name = "Lisinopril", Strength = "10mg", Form = "Tablet" } }
        };

        var conditions = new List<Condition>
        {
            new() { Name = "Essential Hypertension", ClinicalStatus = "Active", OnsetDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        };

        var vitals = new List<Observation>
        {
            new() { DisplayName = "Blood Pressure", ValueText = "120/80", RefRangeText = "< 120/80 mmHg", IsAbnormal = false, ObservedAt = DateTime.UtcNow }
        };

        var html = ClinicalSummaryGenerator.GenerateHtml(patient, conditions, allergies, meds, vitals);

        Assert.Contains("Jane", html);
        Assert.Contains("Doe", html);
        Assert.Contains("MRN-TEST-1234", html);
        Assert.Contains("Penicillin", html);
        Assert.Contains("Lisinopril", html);
        Assert.Contains("Essential Hypertension", html);
        Assert.Contains("Blood Pressure", html);
    }

    [Fact]
    public async Task AppointmentReminderWorker_DispatchesRemindersForUpcomingAppointments()
    {
        var dbName = $"reminder_test_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<MedicalPortalDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));
        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var person = new Person { FirstName = "Dana", LastName = "Rivera", DateOfBirth = new DateTime(1988, 3, 14, 0, 0, 0, DateTimeKind.Utc) };
            var patient = new Patient { Person = person, Mrn = "MRN-100" };
            var provider = new Provider { FirstName = "Amara", LastName = "Patel", Specialty = "Family Medicine" };
            db.Persons.Add(person);
            db.Patients.Add(patient);
            db.Providers.Add(provider);

            // Appointment tomorrow
            db.Appointments.Add(new Appointment
            {
                Patient = patient,
                Provider = provider,
                StartAt = DateTime.UtcNow.AddHours(24),
                EndAt = DateTime.UtcNow.AddHours(24).AddMinutes(30),
                Status = AppointmentStatus.CONFIRMED,
                ReasonForVisit = "Annual Wellness Visit"
            });

            await db.SaveChangesAsync();
        }

        var worker = new AppointmentReminderWorker(sp, NullLogger<AppointmentReminderWorker>.Instance);
        var dispatched = await worker.ProcessUpcomingAppointmentRemindersAsync();

        Assert.Equal(1, dispatched);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var log = await db.AuditLogs.FirstOrDefaultAsync(l => l.Action == "APPOINTMENT_REMINDER_DISPATCHED");
            Assert.NotNull(log);
            Assert.Contains("Annual Wellness Visit", log.MetadataJson);
        }

        // Running again should be idempotent (0 newly dispatched)
        var secondRun = await worker.ProcessUpcomingAppointmentRemindersAsync();
        Assert.Equal(0, secondRun);
    }
}
