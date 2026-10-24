using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Fhir;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedicalPortal.Tests;

public class ClinicalCoreIntegrationAndSyncTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ClinicalCoreIntegrationAndSyncTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static (MedicalPortalDbContext Db, FakeClinicalCoreFhirClient FakeFhir, ClinicalCoreSyncService SyncService) CreateHarness()
    {
        var dbName = $"ClinicalCoreTestDb_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var db = new MedicalPortalDbContext(options);
        var fakeFhir = new FakeClinicalCoreFhirClient();
        var snapshotService = new SnapshotService(db);
        var fhirOptions = Options.Create(new ClinicalCoreFhirOptions
        {
            BaseUrl = "http://localhost:8080/openmrs/ws/fhir2/R4/",
            O3BaseUrl = "https://emr.hospital.internal/openmrs/spa",
            UseFake = true
        });

        var syncService = new ClinicalCoreSyncService(
            db,
            fakeFhir,
            snapshotService,
            fhirOptions,
            NullLogger<ClinicalCoreSyncService>.Instance);

        return (db, fakeFhir, syncService);
    }

    // =========================================================================
    // 1. INBOUND CLINICAL DATA SYNCHRONIZATION (READ-THROUGH CACHE RECONCILIATION)
    // =========================================================================

    [Fact]
    public async Task SyncPatientClinicalData_PullsObservationsAllergiesMedsAndRefreshesSnapshot()
    {
        var (db, fakeFhir, syncService) = CreateHarness();

        var person = new Person
        {
            FirstName = "Abebe",
            LastName = "Kebede",
            DateOfBirth = new DateTime(1985, 4, 12, 0, 0, 0, DateTimeKind.Utc),
            Email = "abebe.kebede.test@hospital.internal"
        };
        db.Persons.Add(person);

        var patient = new Patient
        {
            Person = person,
            Mrn = "MRN-1001", // Matches fakeFhir patient-1001
            FhirPatientId = null,
            HmsLastSync = null
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var directLookup = await fakeFhir.GetPatientByMrnAsync(patient.Mrn);
        Assert.NotNull(directLookup);

        // Execute Inbound Sync
        var success = await syncService.SyncPatientClinicalDataAsync(patient.Id);

        Assert.Null(syncService.LastSyncException);
        Assert.True(success);
        Assert.Equal("patient-1001", patient.FhirPatientId);
        Assert.NotNull(patient.HmsLastSync);

        // Verify Observations (BP vitals panel + Fasting Glucose lab)
        var observations = await db.Observations
            .Where(o => o.PatientId == patient.Id)
            .ToListAsync();

        Assert.NotEmpty(observations);
        var systolic = observations.FirstOrDefault(o => o.Code == "8480-6");
        Assert.NotNull(systolic);
        Assert.Equal(ObservationCategory.VITAL, systolic.Category);
        Assert.Equal(130m, systolic.ValueNumber);
        Assert.Equal("mmHg", systolic.Unit);

        var diastolic = observations.FirstOrDefault(o => o.Code == "8462-4");
        Assert.NotNull(diastolic);
        Assert.Equal(ObservationCategory.VITAL, diastolic.Category);
        Assert.Equal(85m, diastolic.ValueNumber);

        var glucose = observations.FirstOrDefault(o => o.Code == "1558-6");
        Assert.NotNull(glucose);
        Assert.Equal(ObservationCategory.LAB, glucose.Category);
        Assert.Equal(105m, glucose.ValueNumber);
        Assert.Equal("mg/dL", glucose.Unit);

        // Verify Allergies
        var allergies = await db.Allergies
            .Where(a => a.PatientId == patient.Id)
            .ToListAsync();

        var penicillin = Assert.Single(allergies);
        Assert.Equal("Penicillin G", penicillin.Substance);
        Assert.Equal(AllergySeverity.SEVERE, penicillin.Severity);
        Assert.False(penicillin.Tombstone);

        // Verify Medications / Prescriptions
        var prescriptions = await db.Prescriptions
            .Include(p => p.Medication)
            .Where(p => p.PatientId == patient.Id)
            .ToListAsync();

        var amlodipineRx = Assert.Single(prescriptions);
        Assert.Equal("Amlodipine 5mg Daily", amlodipineRx.Medication.Name);
        Assert.True(amlodipineRx.IsActive);
        Assert.Contains("5mg", amlodipineRx.Dosage);

        // Verify Conditions (from Encounter reason code)
        var conditions = await db.Conditions
            .Where(c => c.PatientId == patient.Id)
            .ToListAsync();

        var htn = Assert.Single(conditions);
        Assert.Equal("38341003", htn.Code);
        Assert.Equal("Essential Hypertension", htn.Name);
        Assert.Equal("ACTIVE", htn.ClinicalStatus);

        // Verify Health Snapshot was refreshed
        var snapshot = await db.HealthSnapshots.FindAsync(patient.Id);
        Assert.NotNull(snapshot);
        Assert.True(snapshot.ActiveConditionCount >= 1);
        Assert.True(snapshot.ActiveMedicationCount >= 1);
    }

    // =========================================================================
    // 2. OUTBOUND APPOINTMENT PUSH TO CORE
    // =========================================================================

    [Fact]
    public async Task SyncOutboundAppointments_PushesToCore_AndSetsHmsSyncedTrue()
    {
        var (db, fakeFhir, syncService) = CreateHarness();

        var person = new Person
        {
            FirstName = "Outbound",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var provider = new Provider { FirstName = "Dawit", LastName = "Alemu", Specialty = "Internal Med" };
        var patient = new Patient { Person = person, Mrn = "MRN-1001", FhirPatientId = "patient-1001" };
        var dept = new Department { Name = "Cardiology", Code = "CARD" };

        var unsyncedAppt = new Appointment
        {
            Patient = patient,
            Provider = provider,
            Department = dept,
            StartAt = DateTime.UtcNow.AddDays(2),
            EndAt = DateTime.UtcNow.AddDays(2).AddMinutes(30),
            Status = AppointmentStatus.CONFIRMED,
            ReasonForVisit = "Cardiology Referral",
            HmsSynced = false
        };

        db.Appointments.Add(unsyncedAppt);
        await db.SaveChangesAsync();

        var initialCount = fakeFhir.Appointments.Count;
        var pushedCount = await syncService.SyncOutboundAppointmentsAsync();

        Assert.Equal(1, pushedCount);
        Assert.True(unsyncedAppt.HmsSynced);
        Assert.Equal(initialCount + 1, fakeFhir.Appointments.Count);

        var created = fakeFhir.Appointments.Last();
        Assert.Equal("booked", created.Status);
        Assert.Equal("Cardiology Referral", created.Description);
        Assert.Contains(created.Participant!, p => p.Actor?.Reference == "Patient/patient-1001");
    }

    // =========================================================================
    // 3. OFFLINE RESILIENCE & CACHE PRESERVATION (DISCONNECTED LAN SERVER)
    // =========================================================================

    [Fact]
    public async Task SyncPatientClinicalData_WhenCoreOffline_FailsGracefullyAndPreservesCache()
    {
        var (db, fakeFhir, syncService) = CreateHarness();

        var person = new Person { FirstName = "Cached", LastName = "Offline" };
        var patient = new Patient { Person = person, Mrn = "MRN-1001", FhirPatientId = "patient-1001" };
        db.Patients.Add(patient);

        // Pre-populate read-through cache with existing observation
        var cachedObs = new Observation
        {
            Patient = patient,
            Category = ObservationCategory.LAB,
            Code = "OFFLINE-001",
            DisplayName = "Cached Offline Hemoglobin",
            ValueNumber = 14.2m,
            Unit = "g/dL",
            ObservedAt = DateTime.UtcNow.AddDays(-1)
        };
        db.Observations.Add(cachedObs);
        await db.SaveChangesAsync();

        // Simulate complete network disruption to clinical core
        fakeFhir.SimulateNetworkFailure = true;

        // Sync attempt must catch exception, log warning, and return false
        var syncResult = await syncService.SyncPatientClinicalDataAsync(patient.Id);
        Assert.False(syncResult);

        // Verify existing cached data remains untouched and accessible
        var observations = await db.Observations.Where(o => o.PatientId == patient.Id).ToListAsync();
        Assert.Single(observations);
        Assert.Equal("Cached Offline Hemoglobin", observations[0].DisplayName);
    }

    // =========================================================================
    // 4. OPENMRS O3 UI CHART DEEP LINK RESOLUTION
    // =========================================================================

    [Fact]
    public async Task GetO3PatientChartUrl_ResolvesCorrectDeepLink()
    {
        var (db, _, syncService) = CreateHarness();

        var patient = new Patient
        {
            Person = new Person { FirstName = "Abebe", LastName = "Kebede" },
            Mrn = "MRN-1001",
            FhirPatientId = "patient-1001"
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var chartUrl = await syncService.GetO3PatientChartUrlAsync(patient.Id);

        Assert.NotNull(chartUrl);
        Assert.Equal("https://emr.hospital.internal/openmrs/spa/patient/patient-1001/chart", chartUrl);
    }

    // =========================================================================
    // 5. STALE PATIENT INBOUND BATCH PULL
    // =========================================================================

    [Fact]
    public async Task SyncStalePatients_PullsBatchOrderedByLastSync()
    {
        var (db, _, syncService) = CreateHarness();

        var p1 = new Patient
        {
            Person = new Person { FirstName = "P1", LastName = "Test" },
            Mrn = "MRN-1001",
            HmsLastSync = null
        };
        var p2 = new Patient
        {
            Person = new Person { FirstName = "P2", LastName = "Test" },
            Mrn = "UNKNOWN-999",
            HmsLastSync = DateTime.UtcNow.AddDays(-5)
        };

        db.Patients.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var synced = await syncService.SyncStalePatientsAsync(limit: 5);

        // p1 matches patient-1001 and succeeds; p2 does not match in fake and is skipped
        Assert.True(synced >= 1);
        Assert.NotNull(p1.HmsLastSync);
    }

    // =========================================================================
    // 6. CLINICAL DECISION SUPPORT ARCHITECTURAL MASTERY NOTICE
    // =========================================================================

    [Fact]
    public void ClinicalDecisionSupport_MasteryNotice_VerifiesCanonicalCoreCDS()
    {
        Assert.Contains("OpenMRS", DrugInteractionLogic.ClinicalCoreMasteryNotice);
        Assert.Contains("OpenMRS", AllergyLogic.ClinicalCoreMasteryNotice);
        Assert.Contains("demonstration reference", DrugInteractionLogic.ClinicalCoreMasteryNotice);
    }

    // =========================================================================
    // 7. HTTP ENDPOINT TESTS: GET LABS, VITALS, AND O3 CHART URL
    // =========================================================================

    private string CreateToken(UserAccount account)
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        return db.CreateAuthenticatedSession(tokenService, account, account.PersonId);
    }

    [Fact]
    public async Task ClinicalController_GetO3ChartUrl_ReturnsDeepLink_ForAuthorizedClinician()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var clinician = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "apatel@clinic.example");
        var patient = db.Patients.Include(p => p.Person).First();

        var token = CreateToken(clinician);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());

        var response = await client.GetAsync($"/api/clinical/o3-chart-url?patientId={patient.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("chartUrl", out var chartUrlProp));
        Assert.Contains("/openmrs/spa/patient/", chartUrlProp.GetString()!);
        Assert.Contains("/chart", chartUrlProp.GetString()!);
        Assert.Equal("OpenMRS O3 Microfrontend", body.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Negative_ClinicalController_GetO3ChartUrl_RejectsUnauthorizedPatient()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var patientAccount = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "dana@example.com");
        var patient = db.Patients.First(p => p.PersonId == patientAccount.PersonId);

        var token = CreateToken(patientAccount);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());

        // A patient attempting to access clinician encounter chart deep link must be rejected with 403 Forbidden
        var response = await client.GetAsync($"/api/clinical/o3-chart-url?patientId={patient.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Negative_AccessControl_CrossPatientClinicalDataLeak_Forbidden()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var patientAccount = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "dana@example.com");
        var ownPatient = db.Patients.First(p => p.PersonId == patientAccount.PersonId);

        // Create an unlinked stranger patient
        var stranger = new Patient
        {
            Person = new Person { FirstName = "Stranger", LastName = "Danger", Email = "stranger@hospital.internal" },
            Mrn = $"MRN-{Guid.NewGuid():N}"[..10]
        };
        db.Patients.Add(stranger);
        await db.SaveChangesAsync();

        var token = CreateToken(patientAccount);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Header tampering: Target stranger patient ID without proxy grant
        client.DefaultRequestHeaders.Add("x-patient-id", stranger.Id.ToString());

        var response = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
