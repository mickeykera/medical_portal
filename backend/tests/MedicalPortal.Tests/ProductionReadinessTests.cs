using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class ProductionReadinessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductionReadinessTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (UserAccount account, string token) CreateTestUser(
        MedicalPortalDbContext db,
        ITokenService tokens,
        IPasswordHasher hasher,
        UserRole role,
        Guid? facilityId = null,
        Guid? departmentId = null)
    {
        var email = $"prodtest.{role.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}@clinic.example";
        var person = new Person
        {
            FirstName = "ProdTest",
            LastName = role.ToString(),
            Email = email,
            DateOfBirth = new DateTime(1988, 3, 20, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Persons.Add(person);

        var account = new UserAccount
        {
            Person = person,
            Email = email,
            PasswordHash = hasher.HashPassword("StrongP@ssw0rd!"),
            Role = role,
            IsActive = true
        };
        db.UserAccounts.Add(account);
        db.SaveChanges();

        if (facilityId.HasValue && departmentId.HasValue)
        {
            db.UserMemberships.Add(new UserMembership
            {
                UserAccountId = account.Id,
                FacilityId = facilityId.Value,
                DepartmentId = departmentId.Value,
                Role = role.ToString()
            });
            db.SaveChanges();
        }

        var token = tokens.GenerateAccessToken(account, person.Id);
        return (account, token);
    }

    // =========================================================================
    // 1. ACCESS CONTROL TESTS
    // =========================================================================

    [Fact]
    public void AccessControl_Scheduler_CannotReadClinicalNotesOrResults()
    {
        Assert.False(PermissionMatrix.HasPermission(UserRole.SCHEDULER, AppResource.ClinicalNotes, AppAction.Read));
        Assert.False(PermissionMatrix.HasPermission(UserRole.SCHEDULER, AppResource.ClinicalNotes, AppAction.Write));
        Assert.False(PermissionMatrix.HasPermission(UserRole.SCHEDULER, AppResource.ClinicalResults, AppAction.Read));
        Assert.False(PermissionMatrix.HasPermission(UserRole.SCHEDULER, AppResource.ClinicalResults, AppAction.Write));
        Assert.False(PermissionMatrix.HasPermission(UserRole.SCHEDULER, AppResource.Medications, AppAction.Read));

        // But CAN manage appointments
        Assert.True(PermissionMatrix.HasPermission(UserRole.SCHEDULER, AppResource.Appointments, AppAction.Read));
        Assert.True(PermissionMatrix.HasPermission(UserRole.SCHEDULER, AppResource.Appointments, AppAction.Write));
        Assert.True(PermissionMatrix.HasPermission(UserRole.SCHEDULER, AppResource.Appointments, AppAction.Delete));
    }

    [Fact]
    public void AccessControl_BillingStaff_CannotReadClinicalNotes()
    {
        Assert.False(PermissionMatrix.HasPermission(UserRole.BILLING, AppResource.ClinicalNotes, AppAction.Read));
        Assert.False(PermissionMatrix.HasPermission(UserRole.BILLING, AppResource.ClinicalNotes, AppAction.Write));
        Assert.False(PermissionMatrix.HasPermission(UserRole.BILLING, AppResource.ClinicalResults, AppAction.Read));

        // But CAN access financial and appointment encounters
        Assert.True(PermissionMatrix.HasPermission(UserRole.BILLING, AppResource.Billing, AppAction.Read));
        Assert.True(PermissionMatrix.HasPermission(UserRole.BILLING, AppResource.Billing, AppAction.Write));
        Assert.True(PermissionMatrix.HasPermission(UserRole.BILLING, AppResource.Appointments, AppAction.Read));
    }

    [Fact]
    public void AccessControl_ComplianceAuditor_IsReadOnlyInspection()
    {
        Assert.True(PermissionMatrix.HasPermission(UserRole.COMPLIANCE_AUDITOR, AppResource.AuditCompliance, AppAction.Read));
        Assert.True(PermissionMatrix.HasPermission(UserRole.COMPLIANCE_AUDITOR, AppResource.ClinicalNotes, AppAction.Read));
        Assert.True(PermissionMatrix.HasPermission(UserRole.COMPLIANCE_AUDITOR, AppResource.ClinicalResults, AppAction.Read));

        // Cannot write clinical orders, prescribe, or change appointments
        Assert.False(PermissionMatrix.HasPermission(UserRole.COMPLIANCE_AUDITOR, AppResource.ClinicalNotes, AppAction.Write));
        Assert.False(PermissionMatrix.HasPermission(UserRole.COMPLIANCE_AUDITOR, AppResource.Medications, AppAction.Write));
        Assert.False(PermissionMatrix.HasPermission(UserRole.COMPLIANCE_AUDITOR, AppResource.Appointments, AppAction.Write));
        Assert.False(PermissionMatrix.HasPermission(UserRole.COMPLIANCE_AUDITOR, AppResource.Appointments, AppAction.Delete));
    }

    [Fact]
    public async Task AccessControl_BillingUser_AttemptToReadClinicalNotes_ReturnsForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, billingToken) = CreateTestUser(db, tokens, hasher, UserRole.BILLING);

        var person = new Person { FirstName = "Target", LastName = "Patient", Email = $"t.{Guid.NewGuid():N}@ex.com", DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var patient = new Patient { Person = person, Mrn = $"MRN-BILL-{Guid.NewGuid():N}" };
        db.Persons.Add(person);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", billingToken);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());

        var response = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AccessControl_DepartmentScope_Mismatch_ReturnsForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var org = new Organization { Name = "Springfield Health System", Code = $"ORG-{Guid.NewGuid():N}"[..10] };
        db.Organizations.Add(org);
        var facility = new Facility { OrganizationId = org.Id, Name = "Main Hospital", Code = $"FAC-{Guid.NewGuid():N}"[..10] };
        var deptA = new Department { Facility = facility, Name = "Cardiology", Code = $"DEPTA-{Guid.NewGuid():N}"[..10] };
        var deptB = new Department { Facility = facility, Name = "Oncology", Code = $"DEPTB-{Guid.NewGuid():N}"[..10] };
        db.Facilities.Add(facility);
        db.Departments.AddRange(deptA, deptB);
        await db.SaveChangesAsync();

        // User belongs strictly to deptA
        var (_, nurseToken) = CreateTestUser(db, tokens, hasher, UserRole.NURSE, facility.Id, deptA.Id);

        var person = new Person { FirstName = "Target", LastName = "Patient", Email = $"t.{Guid.NewGuid():N}@ex.com", DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var patient = new Patient { Person = person, Mrn = $"MRN-DEPT-{Guid.NewGuid():N}" };
        db.Persons.Add(person);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nurseToken);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());
        client.DefaultRequestHeaders.Add("x-department-id", deptB.Id.ToString()); // cross-department access attempt

        var response = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AccessControl_BreakGlass_ExpiredOverride_IsDenied()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (clinicianAccount, clinicianToken) = CreateTestUser(db, tokens, hasher, UserRole.CLINICIAN);
        var provider = new Provider { FirstName = "Amara", LastName = "Patel", Specialty = "Emergency", AccountId = clinicianAccount.Id };
        db.Providers.Add(provider);

        var person = new Person { FirstName = "Target", LastName = "Patient", Email = $"t.{Guid.NewGuid():N}@ex.com", DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var patient = new Patient { Person = person, Mrn = $"MRN-EXP-{Guid.NewGuid():N}" };
        db.Persons.Add(person);
        db.Patients.Add(patient);

        // Add an expired break glass override (expired 10 minutes ago)
        db.BreakGlassOverrides.Add(new BreakGlassOverride
        {
            ClinicianUserAccountId = clinicianAccount.Id,
            PatientId = patient.Id,
            Reason = "Past trauma event now expired",
            GrantedAt = DateTime.UtcNow.AddMinutes(-70),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clinicianToken);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());

        // Accessing patient record without care-team membership and expired break-glass must be 403 Forbidden
        var response = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // 2. BILLING MATH TESTS
    // =========================================================================

    [Fact]
    public void BillingMath_Fifo_MultiInvoice_PartialAllocation()
    {
        var patientId = Guid.NewGuid();
        var inv1 = new Statement { Id = Guid.NewGuid(), PatientId = patientId, TotalCents = 5000, AmountPaidCents = 0, BalanceCents = 5000, DueAt = DateTime.UtcNow.AddDays(5) };
        var inv2 = new Statement { Id = Guid.NewGuid(), PatientId = patientId, TotalCents = 7500, AmountPaidCents = 0, BalanceCents = 7500, DueAt = DateTime.UtcNow.AddDays(10) };
        var inv3 = new Statement { Id = Guid.NewGuid(), PatientId = patientId, TotalCents = 10000, AmountPaidCents = 0, BalanceCents = 10000, DueAt = DateTime.UtcNow.AddDays(15) };

        var statements = new List<Statement> { inv3, inv1, inv2 }; // unsorted

        // Pay $150.00 (15,000 cents)
        var allocations = BillingLogic.AllocatePaymentFifo(statements, 15000);

        Assert.Equal(3, allocations.Count);

        // Inv 1 ($50.00) fully paid
        Assert.Equal(inv1.Id, allocations[0].StatementId);
        Assert.Equal(5000, allocations[0].AllocatedCents);
        Assert.Equal(0, allocations[0].RemainingStatementBalanceCents);
        Assert.Equal(StatementStatus.PAID, allocations[0].NewStatus);

        // Inv 2 ($75.00) fully paid
        Assert.Equal(inv2.Id, allocations[1].StatementId);
        Assert.Equal(7500, allocations[1].AllocatedCents);
        Assert.Equal(0, allocations[1].RemainingStatementBalanceCents);
        Assert.Equal(StatementStatus.PAID, allocations[1].NewStatus);

        // Inv 3 ($100.00) gets remaining $25.00 (2,500 cents)
        Assert.Equal(inv3.Id, allocations[2].StatementId);
        Assert.Equal(2500, allocations[2].AllocatedCents);
        Assert.Equal(7500, allocations[2].RemainingStatementBalanceCents);
        Assert.Equal(StatementStatus.PARTIALLY_PAID, allocations[2].NewStatus);
    }

    [Fact]
    public void BillingMath_Fifo_Overpayment_LeavesZeroBalance()
    {
        var inv = new Statement { Id = Guid.NewGuid(), TotalCents = 6000, AmountPaidCents = 0, BalanceCents = 6000, DueAt = DateTime.UtcNow.AddDays(5) };

        // Pay $100.00 (10,000 cents) against $60.00 balance
        var allocations = BillingLogic.AllocatePaymentFifo(new[] { inv }, 10000);

        Assert.Single(allocations);
        Assert.Equal(6000, allocations[0].AllocatedCents);
        Assert.Equal(0, allocations[0].RemainingStatementBalanceCents);
        Assert.Equal(0, inv.BalanceCents);
        Assert.Equal(6000, inv.AmountPaidCents);
        Assert.Equal(StatementStatus.PAID, inv.Status);
    }

    [Fact]
    public void BillingMath_Fifo_ZeroOrNegativePayment_ThrowsBillingException()
    {
        var inv = new Statement { TotalCents = 5000, BalanceCents = 5000 };
        Assert.Throws<BillingException>(() => BillingLogic.AllocatePaymentFifo(new[] { inv }, 0));
        Assert.Throws<BillingException>(() => BillingLogic.AllocatePaymentFifo(new[] { inv }, -100));
    }

    [Fact]
    public void BillingMath_InstallmentSchedule_UnevenCents_MatchesExactTotal()
    {
        // $100.00 (10,000 cents) split into 3 installments
        var schedule = BillingLogic.BuildSchedule(10000, 3, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(3, schedule.Count);
        Assert.Equal(3334, schedule[0].AmountCents); // remainder added to first installment
        Assert.Equal(3333, schedule[1].AmountCents);
        Assert.Equal(3333, schedule[2].AmountCents);

        var sum = schedule.Sum(s => s.AmountCents);
        Assert.Equal(10000, sum);
    }

    [Fact]
    public void BillingMath_InstallmentSchedule_SevenInstallments_PreservesTotalExactly()
    {
        // $350.50 (35,050 cents) split into 7 installments
        var schedule = BillingLogic.BuildSchedule(35050, 7, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(7, schedule.Count);
        var sum = schedule.Sum(s => s.AmountCents);
        Assert.Equal(35050, sum);
    }

    [Fact]
    public void BillingMath_RevertAllocation_RestoresBalanceAndStatus()
    {
        var inv = new Statement { TotalCents = 10000, AmountPaidCents = 10000, BalanceCents = 0, Status = StatementStatus.PAID };

        // Partial refund of $30.00 (3,000 cents)
        BillingLogic.RevertAllocation(inv, 3000);

        Assert.Equal(7000, inv.AmountPaidCents);
        Assert.Equal(3000, inv.BalanceCents);
        Assert.Equal(StatementStatus.PARTIALLY_PAID, inv.Status);

        // Full remaining refund of $70.00 (7,000 cents)
        BillingLogic.RevertAllocation(inv, 7000);
        Assert.Equal(0, inv.AmountPaidCents);
        Assert.Equal(10000, inv.BalanceCents);
        Assert.Equal(StatementStatus.OPEN, inv.Status);
    }

    // =========================================================================
    // 3. SCHEDULING TESTS (DST & TEMPLATES)
    // =========================================================================

    [Fact]
    public void Scheduling_DstSpringForward_ConvertsCorrectly()
    {
        var tz = SchedulingLogic.ResolveTimeZone("America/New_York");

        // 2026 Spring Forward in America/New_York is March 8, 2026 (clocks jump 2:00 -> 3:00)
        var localBefore = new DateTime(2026, 3, 8, 1, 30, 0);
        var utcBefore = SchedulingLogic.ConvertLocalToUtc(localBefore, tz);

        // Local 1:30 AM EST is UTC 6:30 AM (UTC-5)
        Assert.Equal(new DateTime(2026, 3, 8, 6, 30, 0, DateTimeKind.Utc), utcBefore);

        var localAfter = new DateTime(2026, 3, 8, 3, 30, 0);
        var utcAfter = SchedulingLogic.ConvertLocalToUtc(localAfter, tz);

        // Local 3:30 AM EDT is UTC 7:30 AM (UTC-4)
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), utcAfter);

        // Duration in UTC between 1:30 AM and 3:30 AM on Spring Forward day is exactly 1 hour, not 2 hours!
        Assert.Equal(TimeSpan.FromHours(1), utcAfter - utcBefore);
    }

    [Fact]
    public void Scheduling_DstFallBack_ConvertsCorrectly()
    {
        var tz = SchedulingLogic.ResolveTimeZone("America/New_York");

        // 2026 Fall Back in America/New_York is November 1, 2026 (clocks repeat 1:00 -> 2:00)
        var localDate = new DateTime(2026, 11, 1, 12, 0, 0); // Noon
        var utcDate = SchedulingLogic.ConvertLocalToUtc(localDate, tz);

        // Local 12:00 PM EST is UTC 17:00 PM (UTC-5)
        Assert.Equal(new DateTime(2026, 11, 1, 17, 0, 0, DateTimeKind.Utc), utcDate);
    }

    [Fact]
    public void Scheduling_WeeklyTemplate_OverlappingBlockedTimes_SuppressesSlots()
    {
        var providerId = Guid.NewGuid();
        var candidateStart = new DateTime(2026, 6, 10, 11, 0, 0, DateTimeKind.Utc);
        var candidateEnd = new DateTime(2026, 6, 10, 11, 30, 0, DateTimeKind.Utc);

        // Overlapping blocks: 10:00 - 11:15 and 11:00 - 12:00
        var blocks = new List<SchedulingLogic.TimeSlot>
        {
            new(new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 10, 11, 15, 0, DateTimeKind.Utc)),
            new(new DateTime(2026, 6, 10, 11, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc))
        };

        var check = SchedulingLogic.CheckSlot(
            new SchedulingLogic.TimeSlot(candidateStart, candidateEnd),
            busySlots: Array.Empty<SchedulingLogic.TimeSlot>(),
            roomBusySlots: null,
            blockedSlots: blocks);

        Assert.False(check.Ok);
        Assert.Equal("blocked_time", check.Problem);
    }

    // =========================================================================
    // 4. CONCURRENCY TESTS (DOUBLE-BOOKING & PAYMENTS)
    // =========================================================================

    [Fact]
    public async Task Concurrency_DoubleBookingPrevention_OnlyOneTransactionSucceeds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (patientAccount, patientToken) = CreateTestUser(db, tokens, hasher, UserRole.PATIENT);
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.PersonId == patientAccount.PersonId);
        if (patient == null)
        {
            patient = new Patient { PersonId = patientAccount.PersonId, Mrn = $"MRN-CONC-{Guid.NewGuid():N}" };
            db.Patients.Add(patient);
            await db.SaveChangesAsync();
        }

        var provider = new Provider { FirstName = "Sarah", LastName = "Lin", Specialty = "Pediatrics" };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();

        // Establish care team relationship
        db.CareTeams.Add(new PatientCareTeam { PatientId = patient.Id, ProviderId = provider.Id, Role = "Primary" });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());

        var slotStart = DateTime.UtcNow.AddDays(7).Date.AddHours(14); // 2:00 PM UTC
        var slotEnd = slotStart.AddMinutes(30);

        var requestBody = new
        {
            providerId = provider.Id.ToString(),
            startAt = slotStart.ToString("o"),
            endAt = slotEnd.ToString("o"),
            reasonForVisit = "Concurrent booking test"
        };

        // Fire 8 concurrent booking requests for the identical provider and time slot
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync("/api/appointments", requestBody))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var nonSuccessResponses = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();

        // Exactly 1 request must win and succeed; all competing requests must be rejected (no duplicate bookings)
        Assert.Equal(1, successCount);
        Assert.Equal(tasks.Count - 1, nonSuccessResponses.Count);
        Assert.All(nonSuccessResponses, r => Assert.NotEqual(HttpStatusCode.Created, r.StatusCode));

        // Verify database contains strictly 1 appointment for this slot
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var appointments = await verifyDb.Appointments
            .Where(a => a.ProviderId == provider.Id && a.StartAt == slotStart)
            .ToListAsync();

        Assert.Single(appointments);
    }
}
