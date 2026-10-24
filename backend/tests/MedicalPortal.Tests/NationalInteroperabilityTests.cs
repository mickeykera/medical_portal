using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Interoperability.Dhis2;
using MedicalPortal.Infrastructure.Interoperability.Echis;
using MedicalPortal.Infrastructure.Interoperability.Mfr;
using MedicalPortal.Infrastructure.Interoperability.Mpi;
using MedicalPortal.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedicalPortal.Tests;

public class NationalInteroperabilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NationalInteroperabilityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static (
        MedicalPortalDbContext Db,
        FakeDhis2Client FakeDhis2,
        Dhis2AggregateService Dhis2Service,
        FakeEchisClient FakeEchis,
        EchisReferralService EchisService,
        FakeMfrClient FakeMfr,
        MfrSyncService MfrService,
        PatientDeduplicationService MpiService,
        IOptions<Dhis2Options> Dhis2Options,
        IOptions<EchisOptions> EchisOptions
    ) CreateHarness()
    {
        var dbName = $"NationalInteropDb_{Guid.NewGuid():N}";
        var dbOptions = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var db = new MedicalPortalDbContext(dbOptions);

        var dhis2Opts = Options.Create(new Dhis2Options
        {
            DefaultDataSetId = "HMIS-MOH-MONTHLY",
            ExportHmacSecret = "Test-Dhis2-Export-Secret-2026",
            UseFake = true
        });
        var fakeDhis2 = new FakeDhis2Client();
        var dhis2Service = new Dhis2AggregateService(db, fakeDhis2, dhis2Opts, NullLogger<Dhis2AggregateService>.Instance);

        var echisOpts = Options.Create(new EchisOptions
        {
            Enabled = true,
            FacilityCode = "ADOT-01",
            UseFake = true
        });
        var fakeEchis = new FakeEchisClient();
        var echisService = new EchisReferralService(db, fakeEchis, echisOpts, NullLogger<EchisReferralService>.Instance);

        var fakeMfr = new FakeMfrClient();
        var mfrService = new MfrSyncService(db, fakeMfr, NullLogger<MfrSyncService>.Instance);

        var mpiService = new PatientDeduplicationService(db, NullLogger<PatientDeduplicationService>.Instance);

        return (db, fakeDhis2, dhis2Service, fakeEchis, echisService, fakeMfr, mfrService, mpiService, dhis2Opts, echisOpts);
    }

    // =========================================================================
    // 1. DHIS2 AGGREGATE EXTRACTOR & REPORT GENERATION
    // =========================================================================

    [Fact]
    public async Task Dhis2_GenerateDraftDataset_AggregatesLocalFacilityIndicators()
    {
        var (db, _, dhis2Service, _, _, _, _, _, _, _) = CreateHarness();

        // Seed patient, appointment, and form response
        var person = new Person { FirstName = "Tewodros", LastName = "Kassahun", Email = "ted@example.com" };
        db.Persons.Add(person);
        var patient = new Patient { Person = person, Mrn = "MRN-101" };
        db.Patients.Add(patient);
        db.Appointments.Add(new Appointment { Patient = patient, StartAt = DateTime.UtcNow, EndAt = DateTime.UtcNow.AddMinutes(30) });
        db.FormResponses.Add(new FormResponse { Patient = patient, FormDefinitionId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        var report = await dhis2Service.GenerateDraftDatasetAsync("201902");

        Assert.NotNull(report);
        Assert.Equal("201902", report.Period);
        Assert.Equal(Dhis2ReportStatus.Draft, report.Status);

        var values = JsonSerializer.Deserialize<List<Dhis2DataValueDto>>(report.DataValuesJson);
        Assert.NotNull(values);
        Assert.Contains(values, v => v.DataElement == "DE-OPD-TOTAL" && v.Value == "1");
        Assert.Contains(values, v => v.DataElement == "DE-PATIENT-NEW" && v.Value == "1");
        Assert.Contains(values, v => v.DataElement == "DE-FORMS-SUBMITTED" && v.Value == "1");
    }

    // =========================================================================
    // 2. DHIS2 DRY RUN VALIDATION
    // =========================================================================

    [Fact]
    public async Task Dhis2_ExecuteDryRun_ValidatesWithoutMutatingProductionDataset()
    {
        var (db, fakeDhis2, dhis2Service, _, _, _, _, _, _, _) = CreateHarness();

        var report = await dhis2Service.GenerateDraftDatasetAsync("201902");
        var dryRunResult = await dhis2Service.ExecuteDryRunAsync(report.Id);

        Assert.Equal("SUCCESS", dryRunResult.Status);
        Assert.Contains("Dry run validation completed", dryRunResult.Description);
        Assert.Equal(Dhis2ReportStatus.DryRun, report.Status);
        Assert.Single(fakeDhis2.DryRuns);
        Assert.Empty(fakeDhis2.Submissions); // Dry run must not create an authoritative submission
    }

    // =========================================================================
    // 3. DHIS2 APPROVAL GATE & MANDATORY APPROVAL BEFORE SUBMIT (NEGATIVE TEST)
    // =========================================================================

    [Fact]
    public async Task Dhis2_SubmitReport_RequiresFacilityApproval_RejectsUnapprovedSubmission()
    {
        var (db, fakeDhis2, dhis2Service, _, _, _, _, _, _, _) = CreateHarness();

        var report = await dhis2Service.GenerateDraftDatasetAsync("201902");

        // Negative test: submitting unapproved report throws InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() => dhis2Service.SubmitReportAsync(report.Id));

        // Perform facility leadership approval
        var approved = await dhis2Service.ApproveReportAsync(report.Id, "Dr. Dawit (Medical Director)");
        Assert.True(approved);
        Assert.Equal(Dhis2ReportStatus.FacilityApproved, report.Status);
        Assert.Equal("Dr. Dawit (Medical Director)", report.ApprovedBy);
        Assert.NotNull(report.ApprovedAtUtc);

        // Now submission succeeds!
        var submitResult = await dhis2Service.SubmitReportAsync(report.Id);
        Assert.Equal("SUCCESS", submitResult.Status);
        Assert.Equal(Dhis2ReportStatus.Imported, report.Status);
        Assert.Single(fakeDhis2.Submissions);
    }

    // =========================================================================
    // 4. DHIS2 IDEMPOTENT RE-SEND
    // =========================================================================

    [Fact]
    public async Task Dhis2_SubmitReport_IdempotentReSend_UpdatesExistingWithoutDuplicates()
    {
        var (db, fakeDhis2, dhis2Service, _, _, _, _, _, _, _) = CreateHarness();

        var report = await dhis2Service.GenerateDraftDatasetAsync("201902");
        await dhis2Service.ApproveReportAsync(report.Id, "Medical Director");
        var res1 = await dhis2Service.SubmitReportAsync(report.Id);
        Assert.Equal("SUCCESS", res1.Status);

        // Re-approve and re-send same report
        await dhis2Service.ApproveReportAsync(report.Id, "Medical Director");
        var res2 = await dhis2Service.SubmitReportAsync(report.Id);

        Assert.Equal("SUCCESS", res2.Status);
        Assert.Contains("idempotent update", res2.Description);
        Assert.Single(fakeDhis2.Submissions); // Only 1 entry in store, updated in-place!
    }

    // =========================================================================
    // 5. DHIS2 OFFLINE SIGNED JSON EXPORT
    // =========================================================================

    [Fact]
    public async Task Dhis2_GenerateSignedOfflineExport_ProducesTamperEvidentBundle()
    {
        var (db, _, dhis2Service, _, _, _, _, _, dhis2Opts, _) = CreateHarness();

        var report = await dhis2Service.GenerateDraftDatasetAsync("201902");
        var export = await dhis2Service.GenerateSignedOfflineExportAsync(report.Id);

        Assert.NotNull(export);
        Assert.NotEmpty(export.Signature);
        Assert.NotEmpty(export.PayloadJson);

        // Re-compute HMAC with secret to verify cryptographic integrity
        var secret = dhis2Opts.Value.ExportHmacSecret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{export.DataSetId}:{export.Period}:{export.OrgUnitId}:{export.PayloadJson}"));
        var expectedSig = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        Assert.Equal(expectedSig, export.Signature);

        // Tamper with payload: signature verification fails
        var tamperedPayload = export.PayloadJson + " ";
        var tamperedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{export.DataSetId}:{export.Period}:{export.OrgUnitId}:{tamperedPayload}"));
        var tamperedSig = Convert.ToHexString(tamperedBytes).ToLowerInvariant();
        Assert.NotEqual(expectedSig, tamperedSig);
    }

    // =========================================================================
    // 6. ECHIS BI-DIRECTIONAL COMMUNITY REFERRALS & FEATURE FLAG
    // =========================================================================

    [Fact]
    public async Task Echis_FeatureFlagDisabled_BlocksReferralCreation()
    {
        var (db, _, _, fakeEchis, echisService, _, _, _, _, echisOpts) = CreateHarness();

        echisOpts.Value.Enabled = false; // Disable feature flag

        var input = new CreateOutboundReferralInputDto(
            PatientId: null,
            NationalPatientId: "FAYYA-001",
            PatientFullName: "Hirut Bekele",
            PatientPhone: "+251911998877",
            ReceivingUnit: "Kebele 02 Health Post",
            ReasonForReferral: "Postpartum community follow-up",
            Urgency: "Routine",
            ClinicalSummaryJson: "{}"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() => echisService.CreateOutboundReferralAsync(input));
    }

    [Fact]
    public async Task Echis_InboundCommunityReferral_SyncAndTriageLifecycle()
    {
        var (db, _, _, fakeEchis, echisService, _, _, _, _, _) = CreateHarness();

        // 1. Sync inbound community referrals
        var syncedCount = await echisService.SyncInboundReferralsAsync();
        Assert.Equal(1, syncedCount);

        var inbound = await db.EchisReferrals.FirstOrDefaultAsync(r => r.ReferralNumber == "ECHIS-REF-2026-0001");
        Assert.NotNull(inbound);
        Assert.Equal(EchisReferralDirection.Inbound, inbound.Direction);
        Assert.Equal(EchisReferralStatus.PendingTriage, inbound.Status);
        Assert.Equal("Almaz Kebede", inbound.PatientFullName);

        // 2. Clinician triages referral to Accepted
        var triaged = await echisService.TriageReferralAsync(inbound.Id, EchisReferralStatus.Accepted, "Admitted to high-risk maternity triage");
        Assert.True(triaged);
        Assert.Equal(EchisReferralStatus.Accepted, inbound.Status);
        Assert.Equal("Admitted to high-risk maternity triage", inbound.TriageNotes);

        // Assert eCHIS gateway client received status update
        Assert.True(fakeEchis.StatusUpdates.ContainsKey("ECHIS-REF-2026-0001"));
        Assert.Equal("Accepted", fakeEchis.StatusUpdates["ECHIS-REF-2026-0001"].Status);
    }

    // =========================================================================
    // 7. MASTER FACILITY REGISTRY (MFR) SYNCHRONIZATION
    // =========================================================================

    [Fact]
    public async Task Mfr_SyncFacilitiesWithMfr_ReconcilesLocalFacilityDirectory()
    {
        var (db, _, _, _, _, fakeMfr, mfrService, _, _, _) = CreateHarness();

        // Seed local facility without MFR details
        var org = new Organization { Name = "Adot Health Group", Code = "ADOT" };
        db.Organizations.Add(org);
        var localFacility = new Facility
        {
            Organization = org,
            Name = "Adot Internal Medicine",
            Code = "ADOT-01"
        };
        db.Facilities.Add(localFacility);
        await db.SaveChangesAsync();

        var result = await mfrService.SyncFacilitiesWithMfrAsync();

        Assert.True(result.ReconciledFacilities >= 1);
        Assert.Equal("ETH-AA-004", localFacility.MfrCode);
        Assert.Equal("General Hospital", localFacility.FacilityType);
        Assert.Equal("Addis Ababa", localFacility.Region);
        Assert.Equal("Kirkos", localFacility.Zone);
        Assert.Equal("Woreda 03", localFacility.Woreda);
        Assert.NotNull(localFacility.MfrLastSyncedAtUtc);
    }

    // =========================================================================
    // 8. PATIENT IDENTITY, NATIONAL MPI & DUPLICATE RESOLUTION
    // =========================================================================

    [Fact]
    public async Task Mpi_DetectsDuplicateDemographics_AndMergesPatientsAtomically()
    {
        var (db, _, _, _, _, _, _, mpiService, _, _) = CreateHarness();

        var p1 = new Person
        {
            FirstName = "Abebe",
            LastName = "Tadesse",
            DateOfBirth = new DateTime(1985, 5, 10, 0, 0, 0, DateTimeKind.Utc),
            Phone = "+251911001122",
            Email = "abebe1@test.internal"
        };
        var p2 = new Person
        {
            FirstName = "Abebe",
            LastName = "Tadesse",
            DateOfBirth = new DateTime(1985, 5, 10, 0, 0, 0, DateTimeKind.Utc),
            Phone = "+251911001122",
            Email = "abebe2@test.internal"
        };
        db.Persons.AddRange(p1, p2);

        var pat1 = new Patient { Person = p1, Mrn = "MRN-PRIMARY-01" };
        var pat2 = new Patient { Person = p2, Mrn = "MRN-DUP-02", NationalPatientId = "FAYYA-9999" };
        db.Patients.AddRange(pat1, pat2);

        // Add appointment on duplicate patient
        var appt = new Appointment { Patient = pat2, StartAt = DateTime.UtcNow, EndAt = DateTime.UtcNow.AddMinutes(30) };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        // 1. Scan detects duplicate
        var detected = await mpiService.ScanForDuplicatesAsync();
        Assert.Equal(1, detected);

        var candidates = await mpiService.GetPendingCandidatesAsync();
        Assert.Single(candidates);
        var cand = candidates.First();
        Assert.True(cand.ConfidenceScore >= 0.90m);

        // 2. Resolve via Merge
        var resolved = await mpiService.ResolveDuplicateAsync(cand.Id, DuplicateResolutionStatus.Merged, Guid.NewGuid(), "Demographic match confirmed by records clerk.");
        Assert.True(resolved);

        // Assert child appointment re-keyed to primary patient
        Assert.Equal(pat1.Id, appt.PatientId);

        // Assert Fayya national ID merged to primary patient
        Assert.Equal("FAYYA-9999", pat1.NationalPatientId);

        // Assert audit entry written
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "PATIENT_MERGE");
        Assert.NotNull(audit);
        Assert.Equal(pat1.Id.ToString(), audit.ResourceId);
    }

    // =========================================================================
    // 9. API ENDPOINTS & RBAC ACCESS CONTROL TESTS
    // =========================================================================

    [Fact]
    public async Task Dhis2Controller_GenerateDraft_RequiresAdmin_RejectsUnauthorized()
    {
        var client = _factory.CreateClient();

        // 1. Unauthenticated -> 401
        var res1 = await client.PostAsJsonAsync("/api/interop/dhis2/draft", new { period = "201902" });
        Assert.Equal(HttpStatusCode.Unauthorized, res1.StatusCode);

        // 2. Patient role -> 403 Forbidden
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var (patientAccount, patientToken) = await CreateTestUserAsync(db, tokenService, UserRole.PATIENT);
        var patientReq = new HttpRequestMessage(HttpMethod.Post, "/api/interop/dhis2/draft")
        {
            Content = JsonContent.Create(new { period = "201902" })
        };
        patientReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
        var patientRes = await client.SendAsync(patientReq);
        Assert.Equal(HttpStatusCode.Forbidden, patientRes.StatusCode);

        // 3. Admin role -> 200 OK
        var (adminAccount, adminToken) = await CreateTestUserAsync(db, tokenService, UserRole.ADMIN);
        var adminReq = new HttpRequestMessage(HttpMethod.Post, "/api/interop/dhis2/draft")
        {
            Content = JsonContent.Create(new { period = "201902" })
        };
        adminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminRes = await client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.OK, adminRes.StatusCode);
    }

    private static async Task<(UserAccount Account, string Token)> CreateTestUserAsync(
        MedicalPortalDbContext db,
        ITokenService tokenService,
        UserRole role)
    {
        var person = new Person
        {
            FirstName = "Test",
            LastName = role.ToString(),
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "O",
            Email = $"interop_test_{role}_{Guid.NewGuid():N}@test.internal"
        };
        db.Persons.Add(person);

        var account = new UserAccount
        {
            Person = person,
            Role = role,
            Email = person.Email,
            PasswordHash = "HASH",
            AuthorizationVersion = 1
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokenService, account, person.Id);
        return (account, token);
    }
}
