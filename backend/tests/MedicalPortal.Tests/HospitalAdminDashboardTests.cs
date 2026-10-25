using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class HospitalAdminDashboardTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HospitalAdminDashboardTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (UserAccount account, string token) CreateTestUser(
        MedicalPortalDbContext db,
        ITokenService tokens,
        IPasswordHasher hasher,
        UserRole role,
        Guid? deptId = null,
        Guid? facilityId = null)
    {
        var email = $"test.{role.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}@hospital.example";
        var person = new Person
        {
            FirstName = "Test",
            LastName = role.ToString(),
            Email = email,
            DateOfBirth = new DateTime(1988, 3, 20, 0, 0, 0, DateTimeKind.Utc)
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

        if (deptId.HasValue || facilityId.HasValue)
        {
            db.UserMemberships.Add(new UserMembership
            {
                UserAccountId = account.Id,
                DepartmentId = deptId,
                FacilityId = facilityId,
                Role = role.ToString(),
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);
        return (account, token);
    }

    [Fact]
    public async Task Overview_ReturnsOperationalStats_IncludingMfaAndIntegrations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var client = _factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/overview");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("activeUsersByRole", out var usersByRole));
        Assert.True(json.TryGetProperty("lockedAccountsCount", out _));
        Assert.True(json.TryGetProperty("pendingInvitesCount", out _));
        Assert.True(json.TryGetProperty("mfaAdoption", out var mfa));
        Assert.True(mfa.TryGetProperty("adoptionPercentage", out _));
        Assert.True(json.TryGetProperty("integrationsHealth", out var integrations));
        Assert.True(integrations.TryGetProperty("openMrs", out _));
        Assert.True(integrations.TryGetProperty("hms", out _));
        Assert.True(integrations.TryGetProperty("dhis2", out _));
        Assert.True(integrations.TryGetProperty("paymentGateway", out _));
        Assert.True(json.TryGetProperty("failedJobsAndDeadLetters", out _));
        Assert.True(json.TryGetProperty("lastBackupTime", out _));
    }

    [Fact]
    public async Task RoleAuthorizationMatrix_EnforcesAccessAcrossRoles()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, patientToken) = CreateTestUser(db, tokens, hasher, UserRole.PATIENT);
        var (_, clinicianToken) = CreateTestUser(db, tokens, hasher, UserRole.CLINICIAN);
        var (_, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);

        var client = _factory.CreateClient();

        // 1. Patient -> 403 Forbidden on admin endpoints
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/overview"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/security-policy"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 2. Clinician -> 403 Forbidden on admin endpoints
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/overview"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clinicianToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/integrations"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clinicianToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 3. Admin -> 200 OK on operational endpoints
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/overview"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/security-policy"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
    }

    [Fact]
    public async Task PlatformAdmin_ZeroPhiGuarantee_ProhibitedFromPatientData()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, platAdminToken) = CreateTestUser(db, tokens, hasher, UserRole.PLATFORM_ADMIN);
        var client = _factory.CreateClient();

        // 1. Prohibited from viewing patient proxy grants
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/proxy-grants"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", platAdminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 2. Prohibited from viewing patient record shares
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/shares"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", platAdminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 3. Prohibited from querying patient-specific audit logs
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit-logs?patientId={Guid.NewGuid()}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", platAdminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }
    }

    [Fact]
    public async Task DepartmentAdmin_ScopedToAssignedDepartmentOnly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var srmc = await db.Facilities.Include(f => f.Departments).FirstAsync(f => f.Code == "SRMC");
        var deptA = srmc.Departments.First();
        var deptB = srmc.Departments.Last();

        var (deptAdmin, deptAdminToken) = CreateTestUser(db, tokens, hasher, UserRole.DEPT_ADMIN, deptId: deptA.Id);
        var client = _factory.CreateClient();

        // 1. Listing departments shows only assigned department A
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/departments"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deptAdminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var items = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
            Assert.NotNull(items);
            Assert.Contains(items, d => d.GetProperty("id").GetString() == deptA.Id.ToString());
            Assert.DoesNotContain(items, d => d.GetProperty("id").GetString() == deptB.Id.ToString());
        }

        // 2. Attempting to invite a user to department B returns 403 Forbidden
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/invite"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deptAdminToken);
            req.Content = JsonContent.Create(new
            {
                email = $"invite.b.{Guid.NewGuid():N}@clinic.example",
                role = "NURSE",
                firstName = "Unauth",
                lastName = "DeptB",
                departmentId = deptB.Id
            });
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 3. Attempting to invite a user with role above own (e.g. ADMIN) returns 403 Forbidden
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/invite"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deptAdminToken);
            req.Content = JsonContent.Create(new
            {
                email = $"invite.admin.{Guid.NewGuid():N}@clinic.example",
                role = "ADMIN",
                firstName = "Escalated",
                lastName = "Admin",
                departmentId = deptA.Id
            });
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 4. Dept admin denied access to tenant-wide integrations and billing settings
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/integrations"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deptAdminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/billing-settings"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deptAdminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }
    }

    [Fact]
    public async Task LastAdminProtectionGuard_CannotRemoveOrDisableLastAdmin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Disable any other existing active admins for this test so exactly 1 active admin remains
        var existingAdmins = await db.UserAccounts.Where(u => u.Role == UserRole.ADMIN && u.IsActive).ToListAsync();
        try
        {
            foreach (var ex in existingAdmins)
            {
                ex.IsActive = false;
            }
            db.SaveChanges();

            // Create the sole active admin
            var (soleAdmin, soleAdminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
            var client = _factory.CreateClient();

            // Admin attempting to disable own account is blocked by self-disable guard
            var stepUpToken = tokens.GenerateStepUpToken(soleAdmin.Id, "disable_user");
            using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{soleAdmin.Id}/disable"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", soleAdminToken);
                req.Headers.Add("X-Step-Up-Token", stepUpToken);
                var res = await client.SendAsync(req);
                Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            }

            // Create a secondary admin who attempts to demote soleAdmin
            var (secAdmin, secAdminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
            // Deactivate secAdmin so soleAdmin is the only active one
            secAdmin.IsActive = false;
            db.SaveChanges();

            // Reactivate secAdmin temporarily to attempt demoting soleAdmin
            secAdmin.IsActive = true;
            soleAdmin.IsActive = false; // Now secAdmin is the only active admin
            db.SaveChanges();

            var secStepUp = tokens.GenerateStepUpToken(secAdmin.Id, "update_role");
            using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{secAdmin.Id}/role"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secAdminToken);
                req.Headers.Add("X-Step-Up-Token", secStepUp);
                req.Content = JsonContent.Create(new { role = "BILLING" });
                var res = await client.SendAsync(req);
                Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            }
        }
        finally
        {
            // Restore original active admins so subsequent test suites aren't broken
            foreach (var ex in existingAdmins)
            {
                ex.IsActive = true;
            }
            db.SaveChanges();
        }
    }

    [Fact]
    public async Task StepUpEnforcement_RequiresReAuthForSensitiveOperations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var (targetUser, _) = CreateTestUser(db, tokens, hasher, UserRole.NURSE);
        var client = _factory.CreateClient();

        // 1. Calling MFA reset without step-up token -> 403 Forbidden with step_up_required
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetUser.Id}/reset-mfa"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

            var err = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("step_up_required", err.GetProperty("code").GetString());
        }

        // 2. Perform step-up via POST /api/auth/step-up with admin password
        using (var stepUpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/step-up"))
        {
            stepUpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            stepUpReq.Content = JsonContent.Create(new { password = "StrongP@ss123!", action = "reset_mfa" });
            var stepUpActual = await client.SendAsync(stepUpReq);
            Assert.Equal(HttpStatusCode.OK, stepUpActual.StatusCode);

            var stepUpJson = await stepUpActual.Content.ReadFromJsonAsync<JsonElement>();
            var stepUpToken = stepUpJson.GetProperty("stepUpToken").GetString();
            Assert.NotNull(stepUpToken);

            // 3. Retry MFA reset with X-Step-Up-Token -> 200 OK
            using var retryReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetUser.Id}/reset-mfa");
            retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            retryReq.Headers.Add("X-Step-Up-Token", stepUpToken);

            var retryRes = await client.SendAsync(retryReq);
            Assert.Equal(HttpStatusCode.OK, retryRes.StatusCode);
        }
    }

    [Fact]
    public async Task AdminActions_WriteAuditEntries_AndNotifyAffectedUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var (targetUser, _) = CreateTestUser(db, tokens, hasher, UserRole.NURSE);
        var client = _factory.CreateClient();

        var stepUp = tokens.GenerateStepUpToken(admin.Id, "update_role");

        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{targetUser.Id}/role");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req.Headers.Add("X-Step-Up-Token", stepUp);
        req.Content = JsonContent.Create(new { role = "BILLING" });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // Verify audit entry exists
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var auditLog = await verifyDb.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "ADMIN_USER_ROLE_UPDATED" && a.ResourceId == targetUser.Id.ToString());

        Assert.NotNull(auditLog);
        Assert.Equal(admin.Id, auditLog.ActorUserAccountId);

        // Verify user notification delivery record was queued
        var notif = await verifyDb.NotificationDeliveryRecords
            .OrderByDescending(n => n.CreatedAtUtc)
            .FirstOrDefaultAsync(n => n.RecipientPersonId == targetUser.PersonId);

        Assert.NotNull(notif);
        Assert.Equal("SecurityAlert", notif.Category);
        Assert.Contains("Role Changed", notif.Subject);
    }

    [Fact]
    public async Task UserInvites_InviteFlow_AndCsvImportDryRun()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (admin, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var client = _factory.CreateClient();

        // 1. Create user by invite link (admin never sets or sees password)
        var inviteEmail = $"invited.{Guid.NewGuid():N}@clinic.example";
        using var inviteReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/invite");
        inviteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        inviteReq.Content = JsonContent.Create(new
        {
            email = inviteEmail,
            role = "CLINICIAN",
            firstName = "Almaz",
            lastName = "Bekele",
            dateOfBirth = "1992-07-15"
        });

        var inviteRes = await client.SendAsync(inviteReq);
        Assert.Equal(HttpStatusCode.Created, inviteRes.StatusCode);

        var inviteJson = await inviteRes.Content.ReadFromJsonAsync<JsonElement>();
        var rawToken = inviteJson.GetProperty("inviteToken").GetString();
        Assert.NotNull(rawToken);

        // 2. User accepts invite with own chosen password via public acceptance endpoint
        var acceptRes = await client.PostAsJsonAsync("/api/auth/invites/accept", new
        {
            token = rawToken,
            password = "MySecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, acceptRes.StatusCode);

        // 3. CSV Import dry run
        var csvContent = "email,firstName,lastName,role,departmentCode\n" +
                         $"csv1.{Guid.NewGuid():N}@clinic.example,Sara,Girma,NURSE,\n" +
                         $"csv2.{Guid.NewGuid():N}@clinic.example,Dawit,Tadesse,BILLING,";

        using var csvReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/import-csv");
        csvReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        csvReq.Content = JsonContent.Create(new { csvContent, dryRun = true });

        var csvRes = await client.SendAsync(csvReq);
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);

        var csvJson = await csvRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(csvJson.GetProperty("dryRun").GetBoolean());
        Assert.Equal(2, csvJson.GetProperty("validCount").GetInt32());
    }

    [Fact]
    public async Task Organization_AddisAbabaLocations_ValidatesSubcities()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var client = _factory.CreateClient();

        // 1. Valid Addis Ababa subcity (Bole)
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/locations"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonContent.Create(new
            {
                name = "Bole Medhanialem Clinic",
                subcity = "Bole",
                phone = "+251 11 661 2233"
            });
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }

        // 2. Invalid subcity returns 400 BadRequest
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/locations"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonContent.Create(new
            {
                name = "Unknown District Clinic",
                subcity = "NonExistentSubcity",
                phone = "+251 11 999 0000"
            });
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task TenantSettings_EnabledModules_ReadonlyUnlessPlatformAdmin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var (_, platAdminToken) = CreateTestUser(db, tokens, hasher, UserRole.PLATFORM_ADMIN);
        var client = _factory.CreateClient();

        // 1. Hospital admin attempting to modify enabled modules -> 403 Forbidden
        using (var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/tenant-settings"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonContent.Create(new
            {
                name = "Updated Hospital Name",
                brandColor = "#0284c7",
                enabledModules = new List<string> { "appointments", "billing" }
            });
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 2. Platform admin can update enabled modules -> 200 OK
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/tenant-settings");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", platAdminToken);
            req.Content = JsonContent.Create(new
            {
                name = "Updated Hospital Name",
                brandColor = "#0284c7",
                enabledModules = new List<string> { "appointments", "billing" }
            });
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
        finally
        {
            // Restore default enabled modules so subsequent tests in the suite have full access
            using var restoreReq = new HttpRequestMessage(HttpMethod.Put, "/api/admin/tenant-settings");
            restoreReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", platAdminToken);
            restoreReq.Content = JsonContent.Create(new
            {
                name = "Addis Care Health Network (DEMO)",
                brandColor = "#0284c7",
                enabledModules = new List<string>
                {
                    "appointments", "messaging", "billing", "refills", "proxy", "sharing", "fhir", "findcare", "forms",
                    "interop", "dhis2", "echis", "mfr", "mpi"
                }
            });
            await client.SendAsync(restoreReq);
        }
    }

    [Fact]
    public async Task CrossTenant_Isolation_CannotAccessOrModifyOtherTenantResources()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, adminToken) = CreateTestUser(db, tokens, hasher, UserRole.ADMIN);
        var client = _factory.CreateClient();

        var otherSlug = $"other-{Guid.NewGuid():N}"[..12];
        var otherTenant = new Tenant
        {
            Name = "Other Hospital",
            Slug = otherSlug,
            IsActive = true
        };
        db.Tenants.Add(otherTenant);
        await db.SaveChangesAsync();

        // Springfield admin attempting to query another tenant via header -> 403 Forbidden
        using var crossTenantReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/overview");
        crossTenantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        crossTenantReq.Headers.Add(TenantConstants.TenantSlugHeader, otherSlug);

        var crossTenantRes = await client.SendAsync(crossTenantReq);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRes.StatusCode);

        var errJson = await crossTenantRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenant_mismatch", errJson.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrossDepartment_Isolation_DepartmentAdmin_CannotAccessOrModifyOtherDepartment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var srmc = await db.Facilities.Include(f => f.Departments).FirstAsync(f => f.Code == "SRMC");
        var dept1 = srmc.Departments.First();
        var dept2 = srmc.Departments.Last();

        // deptAdmin is assigned only to dept1
        var (_, deptAdminToken) = CreateTestUser(db, tokens, hasher, UserRole.DEPT_ADMIN, deptId: dept1.Id, facilityId: srmc.Id);

        var client = _factory.CreateClient();

        // Attempting to invite a user into dept2 (unassigned department) -> 403 Forbidden
        using var inviteReq = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/invite");
        inviteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deptAdminToken);
        inviteReq.Content = JsonContent.Create(new
        {
            email = $"crossdept.{Guid.NewGuid():N}@clinic.example",
            role = "NURSE",
            firstName = "Cross",
            lastName = "Dept",
            dateOfBirth = "1990-01-01",
            departmentId = dept2.Id
        });

        var inviteRes = await client.SendAsync(inviteReq);
        Assert.Equal(HttpStatusCode.Forbidden, inviteRes.StatusCode);
    }

    [Fact]
    public void UISmokeTests_AllSections_ScriptsAndAccessibilityGuards()
    {
        var frontendDir = RepoPaths.Frontend;
        Assert.True(Directory.Exists(frontendDir), $"Frontend directory not found at {frontendDir}");

        // 1. index.html checks
        var indexHtml = File.ReadAllText(Path.Combine(frontendDir, "index.html"));
        Assert.Contains("/js/admin-dashboard.js", indexHtml);
        Assert.Contains("/js/app.js", indexHtml);

        var adminDashIdx = indexHtml.IndexOf("/js/admin-dashboard.js", StringComparison.Ordinal);
        var appJsIdx = indexHtml.IndexOf("/js/app.js", StringComparison.Ordinal);
        Assert.True(adminDashIdx < appJsIdx, "admin-dashboard.js should be loaded before app.js");

        // No external CDN scripts
        Assert.DoesNotContain("unpkg.com", indexHtml);
        Assert.DoesNotContain("cdnjs.cloudflare.com", indexHtml);
        Assert.DoesNotContain("cdn.jsdelivr.net", indexHtml);

        // 2. app.js integration checks
        var appJs = File.ReadAllText(Path.Combine(frontendDir, "js", "app.js"));
        Assert.Contains("AdminDashboard.render", appJs);
        Assert.Contains("window.api = api;", appJs);
        Assert.Contains("window.showToast = showToast;", appJs);
        Assert.Contains("PLATFORM_ADMIN", appJs);

        // 3. admin-dashboard.js modular section checks
        var adminJs = File.ReadAllText(Path.Combine(frontendDir, "js", "admin-dashboard.js"));

        // All 8 sections must have concrete implementations
        Assert.Contains("renderOverview", adminJs);          // Section 1: Overview
        Assert.Contains("renderUsers", adminJs);             // Section 2: Users & Access
        Assert.Contains("renderOrganization", adminJs);      // Section 3: Organization
        Assert.Contains("renderAudit", adminJs);             // Section 4: Audit Log
        Assert.Contains("renderSecurity", adminJs);          // Section 5: Security Policy
        Assert.Contains("renderIntegrations", adminJs);      // Section 6: Integrations
        Assert.Contains("renderBilling", adminJs);           // Section 7: Billing Settings
        Assert.Contains("renderTenant", adminJs);            // Section 8: Tenant Settings

        // Strict prohibition of browser native dialogs: window.alert, window.confirm, window.prompt
        Assert.DoesNotContain("window.alert", adminJs);
        Assert.DoesNotContain("window.confirm", adminJs);
        Assert.DoesNotContain("window.prompt", adminJs);
        Assert.DoesNotContain("alert(", adminJs);

        // Modal and typed confirmation checks
        Assert.Contains("showInAppModal", adminJs);
        Assert.Contains("showTypedConfirmModal", adminJs);
        Assert.Contains("DISABLE", adminJs);
        Assert.Contains("ensureStepUpToken", adminJs);

        // Addis Ababa subcities validation list
        var expectedSubcities = new[]
        {
            "Bole", "Kirkos", "Arada", "Yeka", "Lideta", "Nifas Silk-Lafto",
            "Gulele", "Akaky Kaliti", "Kolfe Keranio", "Addis Ketema", "Lemi Kura"
        };
        foreach (var subcity in expectedSubcities)
        {
            Assert.Contains(subcity, adminJs);
        }

        // Dual calendar, ETB currency, and contrast calculation
        Assert.Contains("formatDualDate", adminJs);
        Assert.Contains("formatEtb", adminJs);
        Assert.Contains("calculateContrastRatio", adminJs);

        // 4. Locales verification (en, am, om)
        var enJson = File.ReadAllText(Path.Combine(frontendDir, "locales", "en.json"));
        var amJson = File.ReadAllText(Path.Combine(frontendDir, "locales", "am.json"));
        var omJson = File.ReadAllText(Path.Combine(frontendDir, "locales", "om.json"));

        var adminKeys = new[]
        {
            "\"admin.overview\"",
            "\"admin.users\"",
            "\"admin.organization\"",
            "\"admin.audit\"",
            "\"admin.security\"",
            "\"admin.integrations\"",
            "\"admin.billing\"",
            "\"admin.tenant\""
        };

        foreach (var key in adminKeys)
        {
            Assert.Contains(key, enJson);
            Assert.Contains(key, amJson);
            Assert.Contains(key, omJson);
        }
    }
}
