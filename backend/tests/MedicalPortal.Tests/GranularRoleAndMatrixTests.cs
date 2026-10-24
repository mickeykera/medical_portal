using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class GranularRoleAndMatrixTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GranularRoleAndMatrixTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private MedicalPortalDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MedicalPortalDbContext(options);
    }

    // ==========================================
    // 1. Table-Driven Matrix Theory Tests
    // ==========================================

    public static IEnumerable<object[]> MatrixTestCases()
    {
        // BILLING: Must NOT read clinical notes or clinical results. Can read/write billing, read appointments.
        yield return new object[] { UserRole.BILLING, AppResource.ClinicalNotes, AppAction.Read, false };
        yield return new object[] { UserRole.BILLING, AppResource.ClinicalNotes, AppAction.Write, false };
        yield return new object[] { UserRole.BILLING, AppResource.ClinicalResults, AppAction.Read, false };
        yield return new object[] { UserRole.BILLING, AppResource.Billing, AppAction.Read, true };
        yield return new object[] { UserRole.BILLING, AppResource.Billing, AppAction.Write, true };
        yield return new object[] { UserRole.BILLING, AppResource.Appointments, AppAction.Read, true };
        yield return new object[] { UserRole.BILLING, AppResource.Appointments, AppAction.Write, false };

        // SCHEDULER: Must NOT read clinical results or notes. Can read/write/delete appointments.
        yield return new object[] { UserRole.SCHEDULER, AppResource.ClinicalResults, AppAction.Read, false };
        yield return new object[] { UserRole.SCHEDULER, AppResource.ClinicalNotes, AppAction.Read, false };
        yield return new object[] { UserRole.SCHEDULER, AppResource.Appointments, AppAction.Read, true };
        yield return new object[] { UserRole.SCHEDULER, AppResource.Appointments, AppAction.Write, true };
        yield return new object[] { UserRole.SCHEDULER, AppResource.Appointments, AppAction.Delete, true };
        yield return new object[] { UserRole.SCHEDULER, AppResource.Billing, AppAction.Read, false };

        // LAB: Clinical results only
        yield return new object[] { UserRole.LAB, AppResource.ClinicalResults, AppAction.Read, true };
        yield return new object[] { UserRole.LAB, AppResource.ClinicalResults, AppAction.Write, true };
        yield return new object[] { UserRole.LAB, AppResource.ClinicalNotes, AppAction.Read, false };
        yield return new object[] { UserRole.LAB, AppResource.Billing, AppAction.Read, false };
        yield return new object[] { UserRole.LAB, AppResource.Appointments, AppAction.Read, false };

        // PHARMACIST: Medications and results
        yield return new object[] { UserRole.PHARMACIST, AppResource.Medications, AppAction.Read, true };
        yield return new object[] { UserRole.PHARMACIST, AppResource.Medications, AppAction.Write, true };
        yield return new object[] { UserRole.PHARMACIST, AppResource.ClinicalResults, AppAction.Read, true };
        yield return new object[] { UserRole.PHARMACIST, AppResource.ClinicalNotes, AppAction.Write, false };
        yield return new object[] { UserRole.PHARMACIST, AppResource.Billing, AppAction.Read, false };

        // NURSE: Care delivery (cannot manage billing or prescribe independently)
        yield return new object[] { UserRole.NURSE, AppResource.ClinicalNotes, AppAction.Read, true };
        yield return new object[] { UserRole.NURSE, AppResource.ClinicalNotes, AppAction.Write, true };
        yield return new object[] { UserRole.NURSE, AppResource.ClinicalResults, AppAction.Read, true };
        yield return new object[] { UserRole.NURSE, AppResource.Medications, AppAction.Read, true };
        yield return new object[] { UserRole.NURSE, AppResource.Billing, AppAction.Read, false };

        // PHYSICIAN: Comprehensive clinical access
        yield return new object[] { UserRole.PHYSICIAN, AppResource.ClinicalNotes, AppAction.Read, true };
        yield return new object[] { UserRole.PHYSICIAN, AppResource.ClinicalNotes, AppAction.Write, true };
        yield return new object[] { UserRole.PHYSICIAN, AppResource.ClinicalResults, AppAction.Read, true };
        yield return new object[] { UserRole.PHYSICIAN, AppResource.ClinicalResults, AppAction.Write, true };
        yield return new object[] { UserRole.PHYSICIAN, AppResource.Medications, AppAction.Read, true };
        yield return new object[] { UserRole.PHYSICIAN, AppResource.Medications, AppAction.Write, true };
        yield return new object[] { UserRole.PHYSICIAN, AppResource.Appointments, AppAction.Read, true };
        yield return new object[] { UserRole.PHYSICIAN, AppResource.Billing, AppAction.Read, true };

        // COMPLIANCE_AUDITOR: Inspection access across records, audit admin
        yield return new object[] { UserRole.COMPLIANCE_AUDITOR, AppResource.AuditCompliance, AppAction.Read, true };
        yield return new object[] { UserRole.COMPLIANCE_AUDITOR, AppResource.AuditCompliance, AppAction.Admin, true };
        yield return new object[] { UserRole.COMPLIANCE_AUDITOR, AppResource.ClinicalNotes, AppAction.Read, true };
        yield return new object[] { UserRole.COMPLIANCE_AUDITOR, AppResource.ClinicalResults, AppAction.Read, true };
        yield return new object[] { UserRole.COMPLIANCE_AUDITOR, AppResource.Appointments, AppAction.Read, true };
        yield return new object[] { UserRole.COMPLIANCE_AUDITOR, AppResource.Billing, AppAction.Read, true };
        yield return new object[] { UserRole.COMPLIANCE_AUDITOR, AppResource.ClinicalNotes, AppAction.Write, false };

        // DEPT_ADMIN: Department operations
        yield return new object[] { UserRole.DEPT_ADMIN, AppResource.Appointments, AppAction.Read, true };
        yield return new object[] { UserRole.DEPT_ADMIN, AppResource.Appointments, AppAction.Write, true };
        yield return new object[] { UserRole.DEPT_ADMIN, AppResource.Billing, AppAction.Read, true };
        yield return new object[] { UserRole.DEPT_ADMIN, AppResource.AuditCompliance, AppAction.Read, true };
        yield return new object[] { UserRole.DEPT_ADMIN, AppResource.ClinicalNotes, AppAction.Read, false };

        // SYSTEM_ADMIN & ADMIN: Unrestricted
        yield return new object[] { UserRole.SYSTEM_ADMIN, AppResource.ClinicalNotes, AppAction.Read, true };
        yield return new object[] { UserRole.SYSTEM_ADMIN, AppResource.ClinicalNotes, AppAction.Write, true };
        yield return new object[] { UserRole.SYSTEM_ADMIN, AppResource.ClinicalResults, AppAction.Read, true };
        yield return new object[] { UserRole.SYSTEM_ADMIN, AppResource.Billing, AppAction.Read, true };
        yield return new object[] { UserRole.ADMIN, AppResource.ClinicalNotes, AppAction.Read, true };
        yield return new object[] { UserRole.ADMIN, AppResource.ClinicalResults, AppAction.Read, true };
    }

    [Theory]
    [MemberData(nameof(MatrixTestCases))]
    public void PermissionMatrix_TableDriven_EvaluatesPermissionsCorrectly(
        UserRole role, AppResource resource, AppAction action, bool expectedAllowed)
    {
        var result = PermissionMatrix.HasPermission(role, resource, action);
        Assert.Equal(expectedAllowed, result);
    }

    // ==========================================
    // 2. AccessControlService Unit Tests
    // ==========================================

    [Fact]
    public async Task ResolveAccess_BillingRole_DeniedForClinicalNotes()
    {
        using var db = CreateInMemoryDb();
        var patientId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        db.Persons.Add(new Person { Id = personId, FirstName = "Test", LastName = "Patient" });
        db.Patients.Add(new Patient { Id = patientId, PersonId = personId, Mrn = "MRN-1" });
        await db.SaveChangesAsync();

        var service = new AccessControlService(db);
        var actor = new AccessTokenClaims(Guid.NewGuid(), Guid.NewGuid(), UserRole.BILLING, "sid-billing");

        var decision = await service.ResolveAccessAsync(
            actor, patientId, Permission.viewRecords, AppResource.ClinicalNotes, AppAction.Read);

        Assert.False(decision.Allowed);
        Assert.Contains("permission_denied", decision.Reason);
    }

    [Fact]
    public async Task ResolveAccess_SchedulerRole_DeniedForClinicalResults()
    {
        using var db = CreateInMemoryDb();
        var patientId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        db.Persons.Add(new Person { Id = personId, FirstName = "Test", LastName = "Patient" });
        db.Patients.Add(new Patient { Id = patientId, PersonId = personId, Mrn = "MRN-2" });
        await db.SaveChangesAsync();

        var service = new AccessControlService(db);
        var actor = new AccessTokenClaims(Guid.NewGuid(), Guid.NewGuid(), UserRole.SCHEDULER, "sid-sched");

        var decision = await service.ResolveAccessAsync(
            actor, patientId, Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read);

        Assert.False(decision.Allowed);
        Assert.Contains("permission_denied", decision.Reason);
    }

    [Fact]
    public async Task ResolveAccess_DepartmentScope_Mismatch_ReturnsFalse()
    {
        using var db = CreateInMemoryDb();
        var patientId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        db.Persons.Add(new Person { Id = personId, FirstName = "Test", LastName = "Patient" });
        db.Patients.Add(new Patient { Id = patientId, PersonId = personId, Mrn = "MRN-3" });

        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", Code = "ORG" };
        var fac = new Facility { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "Fac", Code = "FAC" };
        var deptA = new Department { Id = Guid.NewGuid(), FacilityId = fac.Id, Name = "Dept A", Code = "DEPT-A", Specialty = "Cardio" };
        var deptB = new Department { Id = Guid.NewGuid(), FacilityId = fac.Id, Name = "Dept B", Code = "DEPT-B", Specialty = "Peds" };
        db.Organizations.Add(org);
        db.Facilities.Add(fac);
        db.Departments.AddRange(deptA, deptB);

        var staffAccountId = Guid.NewGuid();
        db.UserMemberships.Add(new UserMembership
        {
            Id = Guid.NewGuid(),
            UserAccountId = staffAccountId,
            FacilityId = fac.Id,
            DepartmentId = deptA.Id,
            Role = "SCHEDULER"
        });
        await db.SaveChangesAsync();

        var service = new AccessControlService(db);
        var actor = new AccessTokenClaims(staffAccountId, Guid.NewGuid(), UserRole.SCHEDULER, "sid-scope-mismatch");

        // Attempt to access with Dept B scope -> Should be rejected
        var decision = await service.ResolveAccessAsync(
            actor, patientId, Permission.manageAppointments, AppResource.Appointments, AppAction.Read, departmentId: deptB.Id);

        Assert.False(decision.Allowed);
        Assert.Equal("department_scope_mismatch", decision.Reason);

        // Attempt with Dept A scope -> Should succeed
        var allowedDecision = await service.ResolveAccessAsync(
            actor, patientId, Permission.manageAppointments, AppResource.Appointments, AppAction.Read, departmentId: deptA.Id);

        Assert.True(allowedDecision.Allowed);
    }

    [Fact]
    public async Task ResolveAccess_DepartmentScope_FacilityWideMembership_AllowsAllDepartmentsInFacility()
    {
        using var db = CreateInMemoryDb();
        var patientId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        db.Persons.Add(new Person { Id = personId, FirstName = "Test", LastName = "Patient" });
        db.Patients.Add(new Patient { Id = patientId, PersonId = personId, Mrn = "MRN-4" });

        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", Code = "ORG" };
        var fac1 = new Facility { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "Fac 1", Code = "FAC1" };
        var fac2 = new Facility { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "Fac 2", Code = "FAC2" };
        var dept1 = new Department { Id = Guid.NewGuid(), FacilityId = fac1.Id, Name = "Dept 1", Code = "D1", Specialty = "Cardio" };
        var dept2 = new Department { Id = Guid.NewGuid(), FacilityId = fac2.Id, Name = "Dept 2", Code = "D2", Specialty = "Peds" };
        db.Organizations.Add(org);
        db.Facilities.AddRange(fac1, fac2);
        db.Departments.AddRange(dept1, dept2);

        var staffAccountId = Guid.NewGuid();
        // Facility-wide membership for Facility 1 (DepartmentId = null)
        db.UserMemberships.Add(new UserMembership
        {
            Id = Guid.NewGuid(),
            UserAccountId = staffAccountId,
            FacilityId = fac1.Id,
            DepartmentId = null,
            Role = "NURSE"
        });
        await db.SaveChangesAsync();

        var service = new AccessControlService(db);
        var actor = new AccessTokenClaims(staffAccountId, Guid.NewGuid(), UserRole.NURSE, "sid-fac-nurse");

        // Access Dept 1 (under Facility 1) -> Allowed
        var decision1 = await service.ResolveAccessAsync(
            actor, patientId, Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read, departmentId: dept1.Id);
        Assert.True(decision1.Allowed);

        // Access Dept 2 (under Facility 2) -> Denied
        var decision2 = await service.ResolveAccessAsync(
            actor, patientId, Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read, departmentId: dept2.Id);
        Assert.False(decision2.Allowed);
        Assert.Equal("department_scope_mismatch", decision2.Reason);
    }

    // ==========================================
    // 3. Web Endpoint Integration Tests
    // ==========================================

    [Fact]
    public async Task Endpoint_Billing_CannotReadClinicalNotes_ReturnsForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var billingEmail = $"billing.{Guid.NewGuid():N}@clinic.example";
        var billingAccount = new UserAccount
        {
            Email = billingEmail,
            PasswordHash = "hashed",
            Role = UserRole.BILLING,
            Person = new Person
            {
                FirstName = "Bob",
                LastName = "Billing",
                Email = billingEmail,
                DateOfBirth = new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(billingAccount);

        var patientEmail = $"patient.{Guid.NewGuid():N}@patient.example";
        var patientPerson = new Person
        {
            FirstName = "Alice",
            LastName = "Patient",
            Email = patientEmail,
            DateOfBirth = new DateTime(1990, 5, 5, 0, 0, 0, DateTimeKind.Utc)
        };
        var patient = new Patient { Person = patientPerson, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateAccessToken(billingAccount, billingAccount.PersonId);
        var client = _factory.CreateClient();

        // Billing role attempting to read clinical export summary (ClinicalNotes resource)
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/clinical/summary/export");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-patient-id", patient.Id.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_Billing_CanReadStatements_ReturnsSuccess()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var billingEmail = $"billing.ok.{Guid.NewGuid():N}@clinic.example";
        var billingAccount = new UserAccount
        {
            Email = billingEmail,
            PasswordHash = "hashed",
            Role = UserRole.BILLING,
            Person = new Person
            {
                FirstName = "Bill",
                LastName = "Billing",
                Email = billingEmail,
                DateOfBirth = new DateTime(1987, 3, 3, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(billingAccount);

        var patientEmail = $"patient.{Guid.NewGuid():N}@patient.example";
        var patientPerson = new Person
        {
            FirstName = "Alice",
            LastName = "Patient",
            Email = patientEmail,
            DateOfBirth = new DateTime(1990, 5, 5, 0, 0, 0, DateTimeKind.Utc)
        };
        var patient = new Patient { Person = patientPerson, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateAccessToken(billingAccount, billingAccount.PersonId);
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/billing/statements");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-patient-id", patient.Id.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_Scheduler_CannotReadClinicalResults_ReturnsForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var schedEmail = $"scheduler.{Guid.NewGuid():N}@clinic.example";
        var schedAccount = new UserAccount
        {
            Email = schedEmail,
            PasswordHash = "hashed",
            Role = UserRole.SCHEDULER,
            Person = new Person
            {
                FirstName = "Sam",
                LastName = "Scheduler",
                Email = schedEmail,
                DateOfBirth = new DateTime(1988, 2, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(schedAccount);

        var patientEmail = $"patient.{Guid.NewGuid():N}@patient.example";
        var patientPerson = new Person
        {
            FirstName = "Pat",
            LastName = "Patient",
            Email = patientEmail,
            DateOfBirth = new DateTime(1991, 3, 3, 0, 0, 0, DateTimeKind.Utc)
        };
        var patient = new Patient { Person = patientPerson, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateAccessToken(schedAccount, schedAccount.PersonId);
        var client = _factory.CreateClient();

        // Scheduler attempting to read clinical labs (ClinicalResults resource)
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/clinical/labs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-patient-id", patient.Id.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_Scheduler_CanReadAppointments_ReturnsSuccess()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var schedEmail = $"scheduler.{Guid.NewGuid():N}@clinic.example";
        var schedAccount = new UserAccount
        {
            Email = schedEmail,
            PasswordHash = "hashed",
            Role = UserRole.SCHEDULER,
            Person = new Person
            {
                FirstName = "Sally",
                LastName = "Scheduler",
                Email = schedEmail,
                DateOfBirth = new DateTime(1989, 4, 4, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(schedAccount);

        var patientEmail = $"patient.{Guid.NewGuid():N}@patient.example";
        var patientPerson = new Person
        {
            FirstName = "Paul",
            LastName = "Patient",
            Email = patientEmail,
            DateOfBirth = new DateTime(1992, 6, 6, 0, 0, 0, DateTimeKind.Utc)
        };
        var patient = new Patient { Person = patientPerson, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateAccessToken(schedAccount, schedAccount.PersonId);
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/appointments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-patient-id", patient.Id.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_DepartmentScope_MismatchHeader_ReturnsForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var org = await db.Organizations
            .Include(o => o.Facilities)
                .ThenInclude(f => f.Departments)
            .FirstAsync(o => o.Code == "SHS");

        var famMedDept = org.Facilities.SelectMany(f => f.Departments).First(d => d.Code == "FAM-MED");
        var cardioDept = org.Facilities.SelectMany(f => f.Departments).First(d => d.Code == "CARD-INT");

        var staffEmail = $"staff.scope.{Guid.NewGuid():N}@clinic.example";
        var staffAccount = new UserAccount
        {
            Email = staffEmail,
            PasswordHash = "hashed",
            Role = UserRole.SCHEDULER,
            Person = new Person
            {
                FirstName = "Scoped",
                LastName = "Staff",
                Email = staffEmail,
                DateOfBirth = new DateTime(1986, 7, 7, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(staffAccount);

        // Assign staff specifically to Family Medicine department only
        db.UserMemberships.Add(new UserMembership
        {
            UserAccountId = staffAccount.Id,
            FacilityId = famMedDept.FacilityId,
            DepartmentId = famMedDept.Id,
            Role = "SCHEDULER"
        });

        var patientEmail = $"patient.{Guid.NewGuid():N}@patient.example";
        var patientPerson = new Person
        {
            FirstName = "Dan",
            LastName = "Patient",
            Email = patientEmail,
            DateOfBirth = new DateTime(1994, 8, 8, 0, 0, 0, DateTimeKind.Utc)
        };
        var patient = new Patient { Person = patientPerson, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateAccessToken(staffAccount, staffAccount.PersonId);
        var client = _factory.CreateClient();

        // 1. Request with mismatched department header (Cardiology) -> Forbidden
        using var mismatchReq = new HttpRequestMessage(HttpMethod.Get, "/api/appointments");
        mismatchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        mismatchReq.Headers.Add("x-patient-id", patient.Id.ToString());
        mismatchReq.Headers.Add("x-department-id", cardioDept.Id.ToString());

        var mismatchRes = await client.SendAsync(mismatchReq);
        Assert.Equal(HttpStatusCode.Forbidden, mismatchRes.StatusCode);

        // 2. Request with matching department header (Family Medicine) -> OK
        using var matchReq = new HttpRequestMessage(HttpMethod.Get, "/api/appointments");
        matchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        matchReq.Headers.Add("x-patient-id", patient.Id.ToString());
        matchReq.Headers.Add("x-department-id", famMedDept.Id.ToString());

        var matchRes = await client.SendAsync(matchReq);
        Assert.Equal(HttpStatusCode.OK, matchRes.StatusCode);
    }
}
