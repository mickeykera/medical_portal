using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class AdminConsoleTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminConsoleTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

private (UserAccount account, string token) CreateTestUser(MedicalPortalDbContext db, ITokenService tokens, IPasswordHasher hasher, UserRole role)
    {
        using var tx = db.BeginDefaultTestTransaction();
        var email = $"test.{role.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}@clinic.example";
        var person = new Person
        {
            FirstName = "Test",
            LastName = role.ToString(),
            Email = email,
            DateOfBirth = new DateTime(1985, 5, 15, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Persons.Add(person);

        var account = new UserAccount
        {
            Person = person,
            Email = email,
            PasswordHash = hasher.HashPassword("StrongP@ss123!"),
            Role = role,
            IsActive = true
        };
        db.UserAccounts.Add(account);
        db.SaveChanges();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);
        tx.Commit();
        return (account, token);
    }

    [Fact]
    public async Task AdminConsole_NonAdmin_Forbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, patientToken) = CreateTestUser(db, tokens, hasher, UserRole.PATIENT);
        var client = _factory.CreateClient();

        // 1. Patient trying to list users
        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Forbidden, res1.StatusCode);

        // 2. Patient trying to create user
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
        req2.Content = JsonContent.Create(new
        {
            email = "unauthorized@clinic.example",
            password = "Password123!",
            role = "SCHEDULER",
            firstName = "Hack",
            lastName = "Attempt",
            dateOfBirth = "1990-01-01"
        });
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Forbidden, res2.StatusCode);
    }

    [Fact]
    public async Task AdminConsole_ListUsers_AdminAllowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var client = _factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?pageSize=10");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(data);
    }

    [Fact]
    public async Task AdminConsole_CreateUser_CreatesAccountAndAudits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var client = _factory.CreateClient();

        var newEmail = $"scheduler.{Guid.NewGuid():N}@clinic.example";
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req.Content = JsonContent.Create(new
        {
            email = newEmail,
            password = "SchedulerPass123!",
            role = "SCHEDULER",
            firstName = "Casey",
            lastName = "Schedule",
            dateOfBirth = "1988-04-12"
        });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        // Verify account exists in DB with hashed password
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var created = await verifyDb.UserAccounts
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Email == newEmail);

        Assert.NotNull(created);
        Assert.Equal(UserRole.SCHEDULER, created.Role);
        Assert.True(created.IsActive);
        Assert.Equal("Casey", created.Person.FirstName);

        // Verify AuditLog entry
        var audit = await verifyDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_USER_CREATED" && a.ResourceId == created.Id.ToString());

        Assert.NotNull(audit);
        Assert.Equal(admin.Id, audit.ActorUserAccountId);
        Assert.Equal("UserAccount", audit.ResourceType);
    }

    [Fact]
    public async Task AdminConsole_DisableAndEnableUser_RevokesSessionsAndBlocksLogin_AndAudits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var (targetUser, _) = CreateTestUser(db, tokens, hasher, UserRole.NURSE);

        // Create an active session for the target user
        var session = new UserSession
        {
            UserAccountId = targetUser.Id,
            Sid = Guid.NewGuid().ToString("N"),
            RefreshTokenHash = "test-hash",
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // 1. Disable user
        using var disableReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetUser.Id}/disable");
        disableReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        disableReq.Headers.Add("X-Step-Up-Token", tokens.GenerateStepUpToken(admin.Id, "disable_user"));
        var disableRes = await client.SendAsync(disableReq);
        Assert.Equal(HttpStatusCode.OK, disableRes.StatusCode);

        // Verify DB: IsActive is false and sessions are deleted/revoked
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updated = await checkDb.UserAccounts.FindAsync(targetUser.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsActive);

        var activeSessions = await checkDb.UserSessions.Where(s => s.UserAccountId == targetUser.Id && s.RevokedAt == null).CountAsync();
        Assert.Equal(0, activeSessions);

        // Verify AuditLog for disable
        var disableAudit = await checkDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_USER_DISABLED" && a.ResourceId == targetUser.Id.ToString());
        Assert.NotNull(disableAudit);
        Assert.Equal(admin.Id, disableAudit.ActorUserAccountId);

        // Login attempt with disabled account -> 401 Unauthorized
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = targetUser.Email,
            password = "StrongP@ss123!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, loginRes.StatusCode);

        // 2. Enable user
        using var enableReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetUser.Id}/enable");
        enableReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var enableRes = await client.SendAsync(enableReq);
        Assert.Equal(HttpStatusCode.OK, enableRes.StatusCode);

        // Verify DB: IsActive is true
        using var recheckScope = _factory.Services.CreateScope();
        var recheckDb = recheckScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var reenabled = await recheckDb.UserAccounts.FindAsync(targetUser.Id);
        Assert.NotNull(reenabled);
        Assert.True(reenabled.IsActive);

        // Verify AuditLog for enable
        var enableAudit = await recheckDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_USER_ENABLED" && a.ResourceId == targetUser.Id.ToString());
        Assert.NotNull(enableAudit);
    }

    [Fact]
    public async Task AdminConsole_UpdateRole_ChangesRoleAndAudits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var (targetUser, _) = CreateTestUser(db, tokens, hasher, UserRole.BILLING);

        var client = _factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{targetUser.Id}/role");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req.Headers.Add("X-Step-Up-Token", tokens.GenerateStepUpToken(admin.Id, "update_role"));
        req.Content = JsonContent.Create(new { role = "COMPLIANCE_AUDITOR" });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updated = await verifyDb.UserAccounts.FindAsync(targetUser.Id);
        Assert.NotNull(updated);
        Assert.Equal(UserRole.COMPLIANCE_AUDITOR, updated.Role);

        var audit = await verifyDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_USER_ROLE_UPDATED" && a.ResourceId == targetUser.Id.ToString());
        Assert.NotNull(audit);
        Assert.Equal(admin.Id, audit.ActorUserAccountId);
    }

    [Fact]
    public async Task AdminConsole_AssignAndRemoveMemberships_AndAudits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var (staff, _) = CreateTestUser(db, tokens, hasher, UserRole.SCHEDULER);

        var facility = await db.Facilities.Include(f => f.Departments).FirstAsync();
        var department = facility.Departments.First();

        var client = _factory.CreateClient();

        // 1. Assign membership
        using var addReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{staff.Id}/memberships");
        addReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        addReq.Content = JsonContent.Create(new
        {
            facilityId = facility.Id,
            departmentId = department.Id,
            role = "SCHEDULER"
        });

        var addRes = await client.SendAsync(addReq);
        Assert.Equal(HttpStatusCode.Created, addRes.StatusCode);

        var addedData = await addRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Guid membershipId = Guid.Parse(addedData.GetProperty("id").GetString()!);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var membership = await verifyDb.UserMemberships.FindAsync(membershipId);
        Assert.NotNull(membership);
        Assert.Equal(facility.Id, membership.FacilityId);
        Assert.Equal(department.Id, membership.DepartmentId);

        // Verify AuditLog for add
        var addAudit = await verifyDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_MEMBERSHIP_ADDED" && a.ResourceId == membershipId.ToString());
        Assert.NotNull(addAudit);

        // 2. Remove membership
        using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/users/{staff.Id}/memberships/{membershipId}");
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var delRes = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        // Verify DB removed
        using var recheckScope = _factory.Services.CreateScope();
        var recheckDb = recheckScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var deleted = await recheckDb.UserMemberships.FindAsync(membershipId);
        Assert.Null(deleted);

        // Verify AuditLog for remove
        var delAudit = await recheckDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_MEMBERSHIP_REMOVED" && a.ResourceId == membershipId.ToString());
        Assert.NotNull(delAudit);
    }

    [Fact]
    public async Task AdminConsole_ManageFacilitiesAndDepartments_AndAudits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);

        var testOrg = new Organization
        {
            Name = "West Health Network",
            Code = $"WHN-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        };
        db.Organizations.Add(testOrg);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // 1. Create Facility
        var facCode = $"FAC-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        using var createFacReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/facilities");
        createFacReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        createFacReq.Content = JsonContent.Create(new
        {
            organizationId = testOrg.Id,
            name = "Lideta Specialty Clinic (DEMO)",
            code = facCode,
            addressLine1 = "Lideta Subcity, House 500",
            city = "Addis Ababa",
            state = "Addis Ababa",
            postalCode = "1000",
            phone = "+251 11 555 5000"
        });

        var createFacRes = await client.SendAsync(createFacReq);
        Assert.Equal(HttpStatusCode.Created, createFacRes.StatusCode);
        var facData = await createFacRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Guid facilityId = Guid.Parse(facData.GetProperty("id").GetString()!);

        // 2. Create Department under Facility
        var deptCode = $"DEP-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        using var createDeptReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/departments");
        createDeptReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        createDeptReq.Content = JsonContent.Create(new
        {
            facilityId = facilityId,
            name = "Neurology Division",
            code = deptCode,
            specialty = "Neurology",
            phone = "+251 11 555 5010"
        });

        var createDeptRes = await client.SendAsync(createDeptReq);
        Assert.Equal(HttpStatusCode.Created, createDeptRes.StatusCode);
        var deptData = await createDeptRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Guid deptId = Guid.Parse(deptData.GetProperty("id").GetString()!);

        // Verify in DB and AuditLogs
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var fac = await verifyDb.Facilities.FindAsync(facilityId);
        Assert.NotNull(fac);
        Assert.Equal("Lideta Specialty Clinic (DEMO)", fac.Name);

        var dept = await verifyDb.Departments.FindAsync(deptId);
        Assert.NotNull(dept);
        Assert.Equal("Neurology Division", dept.Name);

        var facAudit = await verifyDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_FACILITY_CREATED" && a.ResourceId == facilityId.ToString());
        Assert.NotNull(facAudit);

        var deptAudit = await verifyDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_DEPARTMENT_CREATED" && a.ResourceId == deptId.ToString());
        Assert.NotNull(deptAudit);
    }

    [Fact]
    public async Task AdminConsole_ViewAndRevokeProxyGrants_AndAudits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);

        var patientPerson = new Person { FirstName = "Grantor", LastName = "Patient", Email = $"grantor.{Guid.NewGuid():N}@patient.example", DateOfBirth = DateTime.UtcNow.AddYears(-30) };
        var proxyPerson = new Person { FirstName = "Grantee", LastName = "Proxy", Email = $"grantee.{Guid.NewGuid():N}@proxy.example", DateOfBirth = DateTime.UtcNow.AddYears(-28) };
        db.Persons.AddRange(patientPerson, proxyPerson);

        var grant = new ProxyGrant
        {
            Grantor = patientPerson,
            Grantee = proxyPerson,
            CanViewRecords = true,
            ConsentGivenAt = DateTime.UtcNow
        };
        db.ProxyGrants.Add(grant);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // 1. List grants
        using var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/proxy-grants");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var listRes = await client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        // 2. Revoke grant
        using var revokeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/proxy-grants/{grant.Id}/revoke");
        revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var revokeRes = await client.SendAsync(revokeReq);
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // Verify DB: RevokedAt is set
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updated = await verifyDb.ProxyGrants.FindAsync(grant.Id);
        Assert.NotNull(updated);
        Assert.NotNull(updated.RevokedAt);

        // Verify AuditLog
        var audit = await verifyDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_PROXY_GRANT_REVOKED" && a.ResourceId == grant.Id.ToString());
        Assert.NotNull(audit);
        Assert.Equal(admin.Id, audit.ActorUserAccountId);
    }

    [Fact]
    public async Task AdminConsole_ViewAndRevokeRecordShares_AndAudits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);

        var patientPerson = new Person { FirstName = "Share", LastName = "Patient", Email = $"share.{Guid.NewGuid():N}@example.com", DateOfBirth = DateTime.UtcNow.AddYears(-25) };
        var patient = new Patient { Person = patientPerson, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);

        var share = new RecordShare
        {
            Patient = patient,
            Label = "Specialist Referral Consultation",
            CodeHash = "hash123",
            RecipientName = "Dr. Specialist",
            RecipientRole = "Consultant",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            MaxUses = 5,
            UseCount = 1
        };
        db.RecordShares.Add(share);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // 1. List shares
        using var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/shares");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var listRes = await client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        // 2. Revoke share
        using var revokeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/shares/{share.Id}/revoke");
        revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var revokeRes = await client.SendAsync(revokeReq);
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // Verify DB: RevokedAt is set
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updated = await verifyDb.RecordShares.FindAsync(share.Id);
        Assert.NotNull(updated);
        Assert.NotNull(updated.RevokedAt);

        // Verify AuditLog
        var audit = await verifyDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_RECORD_SHARE_REVOKED" && a.ResourceId == share.Id.ToString());
        Assert.NotNull(audit);
        Assert.Equal(admin.Id, audit.ActorUserAccountId);
    }

    [Fact]
    public async Task AdminConsole_AuditLogViewAndVerifyIntegrity()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var client = _factory.CreateClient();

        // 1. Query logs
        using var logsReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit-logs?pageSize=20");
        logsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var logsRes = await client.SendAsync(logsReq);
        Assert.Equal(HttpStatusCode.OK, logsRes.StatusCode);

        // 2. Verify hash chain
        using var verifyReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit-logs/verify");
        verifyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var verifyRes = await client.SendAsync(verifyReq);
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);

        var verifyData = await verifyRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(verifyData.GetProperty("valid").GetBoolean());
    }
}
