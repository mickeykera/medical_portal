using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Payments;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class AuthorizationAndPaymentsSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthorizationAndPaymentsSecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // =========================================================================
    // 1. SEC-05: PAYMENT INTENT REPLAY & MISATTRIBUTION
    // =========================================================================

    [Fact]
    public async Task Payment_IntentReplay_RejectedWithConflict409()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var gateway = (FakePaymentGateway)scope.ServiceProvider.GetRequiredService<IPaymentGateway>();

        var email = $"patient.payreplay.{Guid.NewGuid():N}@patient.example";
        var person = new Person { FirstName = "Abebe", LastName = "Replay", Email = email, DateOfBirth = DateTime.UtcNow.AddYears(-25) };
        var patient = new Patient { Person = person, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        var account = new UserAccount { Person = person, Email = email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.PATIENT, IsActive = true, AuthorizationVersion = 1 };
        var statement = new Statement { Patient = patient, TotalCents = 10000, BalanceCents = 10000, Status = StatementStatus.OPEN };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        db.UserAccounts.Add(account);
        db.Statements.Add(statement);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);

        // Create a confirmed payment intent in FakePaymentGateway for this patient
        var intent = await gateway.CreatePaymentIntentAsync(new PaymentIntentRequest(AmountCents: 5000, PatientId: patient.Id));
        await gateway.ConfirmPaymentAsync(new ConfirmPaymentRequest(intent.PaymentIntentId, PaymentMethodToken: "pm_card_visa"));

        var client = _factory.CreateClient();

        // First submission -> 201 Created
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/billing/payments");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req1.Headers.Add("x-patient-id", patient.Id.ToString());
        req1.Content = JsonContent.Create(new { PaymentIntentId = intent.PaymentIntentId });

        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Second submission of the exact same intent -> 409 Conflict (Replay blocked)
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/billing/payments");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req2.Headers.Add("x-patient-id", patient.Id.ToString());
        req2.Content = JsonContent.Create(new { PaymentIntentId = intent.PaymentIntentId });

        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task Payment_IntentCrossPatientMisattribution_RejectedWithBadRequest()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var gateway = (FakePaymentGateway)scope.ServiceProvider.GetRequiredService<IPaymentGateway>();

        // Patient A (the legitimate intent owner)
        var personA = new Person { FirstName = "Patient", LastName = "A", Email = $"patientA.{Guid.NewGuid():N}@patient.example", DateOfBirth = DateTime.UtcNow.AddYears(-30) };
        var patientA = new Patient { Person = personA, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patientA);

        // Patient B (the attacker / mismatched context)
        var personB = new Person { FirstName = "Patient", LastName = "B", Email = $"patientB.{Guid.NewGuid():N}@patient.example", DateOfBirth = DateTime.UtcNow.AddYears(-30) };
        var patientB = new Patient { Person = personB, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        var accountB = new UserAccount { Person = personB, Email = personB.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.PATIENT, IsActive = true, AuthorizationVersion = 1 };
        var statementB = new Statement { Patient = patientB, TotalCents = 10000, BalanceCents = 10000, Status = StatementStatus.OPEN };

        db.Patients.Add(patientB);
        db.UserAccounts.Add(accountB);
        db.Statements.Add(statementB);
        await db.SaveChangesAsync();

        var tokenB = db.CreateAuthenticatedSession(tokens, accountB, personB.Id);

        // Intent created specifically for Patient A
        var intentForA = await gateway.CreatePaymentIntentAsync(new PaymentIntentRequest(AmountCents: 5000, PatientId: patientA.Id));
        await gateway.ConfirmPaymentAsync(new ConfirmPaymentRequest(intentForA.PaymentIntentId, PaymentMethodToken: "pm_card_visa"));

        var client = _factory.CreateClient();

        // Patient B attempts to submit Patient A's intent -> 400 BadRequest (Context Mismatch)
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/payments");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        req.Headers.Add("x-patient-id", patientB.Id.ToString());
        req.Content = JsonContent.Create(new { PaymentIntentId = intentForA.PaymentIntentId });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // =========================================================================
    // 2. SEC-06: UNENFORCED RECORD-SHARE SCOPES
    // =========================================================================

    [Fact]
    public async Task ShareRedemption_ScopeExceeded_RejectedWithBadRequest()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var person = new Person { FirstName = "Share", LastName = "Patient", Email = $"share.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-35) };
        var patient = new Patient { Person = person, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);

        var plainCode = $"SHR-TEST-{Guid.NewGuid():N}"[..16];
        var codeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainCode)));

        // Share authorized strictly for "medications"
        var share = new RecordShare
        {
            Patient = patient,
            Label = "Medications Only",
            CodeHash = codeHash,
            ScopeJson = JsonSerializer.Serialize(new[] { "medications" }),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            MaxUses = 5,
            UseCount = 0
        };
        db.RecordShares.Add(share);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Caller requests unauthorized category "billing"
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/share");
        req.Content = JsonContent.Create(new { Code = plainCode, Categories = new[] { "medications", "billing" } });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task ShareRedemption_NarrowScope_FiltersUnauthorizedResources()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var person = new Person { FirstName = "Narrow", LastName = "Patient", Email = $"narrow.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-35) };
        var patient = new Patient { Person = person, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);

        // Add a condition and an allergy
        db.Conditions.Add(new Condition { Patient = patient, Code = "I10", Name = "Essential Hypertension" });
        db.Allergies.Add(new Allergy { Patient = patient, Substance = "Penicillin", Severity = AllergySeverity.SEVERE, Reaction = "Anaphylaxis" });

        var plainCode = $"SHR-NARROW-{Guid.NewGuid():N}"[..16];
        var codeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainCode)));

        // Share authorized ONLY for "conditions" (no allergies)
        var share = new RecordShare
        {
            Patient = patient,
            Label = "Conditions Only",
            CodeHash = codeHash,
            ScopeJson = JsonSerializer.Serialize(new[] { "conditions" }),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            MaxUses = 5,
            UseCount = 0
        };
        db.RecordShares.Add(share);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/share");
        req.Content = JsonContent.Create(new { Code = plainCode });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entry");

        // Verify Patient and Condition resources are present, but AllergyIntolerance is absent
        var resourceTypes = entries.EnumerateArray()
            .Select(e => e.GetProperty("resource").GetProperty("resourceType").GetString())
            .ToList();

        Assert.Contains("Patient", resourceTypes);
        Assert.Contains("Condition", resourceTypes);
        Assert.DoesNotContain("AllergyIntolerance", resourceTypes);
    }

    // =========================================================================
    // 3. SEC-07: CALLER-OPTIONAL DEPARTMENT SCOPING
    // =========================================================================

    [Fact]
    public async Task AccessControl_OperationalStaff_WithoutMembership_RejectedWithForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var patientPerson = new Person { FirstName = "Target", LastName = "Patient", Email = $"target.{Guid.NewGuid():N}@patient.example", DateOfBirth = DateTime.UtcNow.AddYears(-20) };
        var patient = new Patient { Person = patientPerson, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);

        // Staff member with role SCHEDULER but NO UserMemberships assigned
        var staffPerson = new Person { FirstName = "Unassigned", LastName = "Staff", Email = $"unassigned.{Guid.NewGuid():N}@staff.example", DateOfBirth = DateTime.UtcNow.AddYears(-28) };
        var staffAccount = new UserAccount { Person = staffPerson, Email = staffPerson.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.SCHEDULER, IsActive = true, AuthorizationVersion = 1 };
        db.UserAccounts.Add(staffAccount);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, staffAccount, staffPerson.Id);

        var client = _factory.CreateClient();

        // Staff attempts to access patient appointments without membership -> 403 Forbidden
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/appointments");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("x-patient-id", patient.Id.ToString());

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // =========================================================================
    // 4. SEC-08: HMS CONFLICTS & DEAD LETTERS ADMIN AUTHORIZATION
    // =========================================================================

    [Fact]
    public async Task HmsWebhook_NonAdminUser_ViewingConflicts_RejectedWithForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Ordinary patient account
        var person = new Person { FirstName = "Pat", LastName = "User", Email = $"pat.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-25) };
        var account = new UserAccount { Person = person, Email = person.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.PATIENT, IsActive = true, AuthorizationVersion = 1 };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);

        var client = _factory.CreateClient();

        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/integrations/hms/conflicts");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Forbidden, res1.StatusCode);

        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/integrations/hms/dead-letters");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Forbidden, res2.StatusCode);
    }

    [Fact]
    public async Task HmsWebhook_NonAdminUser_ResolvingConflict_RejectedWithForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Compliance auditor (can view but cannot resolve mutating records)
        var person = new Person { FirstName = "Auditor", LastName = "User", Email = $"auditor.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-35) };
        var account = new UserAccount { Person = person, Email = person.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.COMPLIANCE_AUDITOR, IsActive = true, AuthorizationVersion = 1 };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);

        var client = _factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/hms/conflicts/{Guid.NewGuid()}/resolve");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { AcceptHms = true, Notes = "Auditor attempt" });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // =========================================================================
    // 5. SEC-09: BOLA / IDOR IN REFILL PROCESSING
    // =========================================================================

    [Fact]
    public async Task Refills_Clinician_ApprovingCrossPatientRefill_ReturnsNotFound()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Clinician
        var docPerson = new Person { FirstName = "Dr", LastName = "Refill", Email = $"doc.refill.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-40) };
        var docAccount = new UserAccount { Person = docPerson, Email = docPerson.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.CLINICIAN, IsActive = true, AuthorizationVersion = 1 };
        var provider = new Provider { Account = docAccount, FirstName = "Dr", LastName = "Refill", Npi = "9988776655" };
        db.Providers.Add(provider);

        // Patient 1 (authorized for Doc)
        var p1Person = new Person { FirstName = "P1", LastName = "Auth", Email = $"p1.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-20) };
        var p1 = new Patient { Person = p1Person, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(p1);
        db.CareTeams.Add(new PatientCareTeam { Patient = p1, Provider = provider });

        // Patient 2 (NOT authorized for Doc) with a refill request
        var p2Person = new Person { FirstName = "P2", LastName = "Victim", Email = $"p2.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-20) };
        var p2 = new Patient { Person = p2Person, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        var med = new Medication { Name = "Metformin 500mg", Strength = "500mg", RxNormCode = "6809" };
        var rx = new Prescription { Patient = p2, Medication = med, Provider = provider, Dosage = "500mg", Frequency = "1 daily", IsActive = true, PrescribedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMonths(1) };
        var p2Refill = new RefillRequest { Prescription = rx, Medication = med, Status = RefillStatus.PENDING, RequestedAt = DateTime.UtcNow };

        db.Patients.Add(p2);
        db.Medications.Add(med);
        db.Prescriptions.Add(rx);
        db.RefillRequests.Add(p2Refill);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, docAccount, docPerson.Id);

        var client = _factory.CreateClient();

        // Clinician sends header for Patient 1, but targets Patient 2's refill ID
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/refills/{p2Refill.Id}/approve");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("x-patient-id", p1.Id.ToString());

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // =========================================================================
    // 6. SEC-10: WEAK PROXY INVITES & UNBOUND ACCEPTANCE
    // =========================================================================

    [Fact]
    public async Task ProxyInvite_CodeEntropy_AtLeast128Bits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var person = new Person { FirstName = "Inviter", LastName = "Person", Email = $"inviter.{Guid.NewGuid():N}@patient.example", DateOfBirth = DateTime.UtcNow.AddYears(-30) };
        var patient = new Patient { Person = person, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        var account = new UserAccount { Person = person, Email = person.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.PATIENT, IsActive = true, AuthorizationVersion = 1 };
        db.Patients.Add(patient);
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);

        var client = _factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/invites");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("x-patient-id", patient.Id.ToString());
        req.Content = JsonContent.Create(new
        {
            Email = $"invitee.{Guid.NewGuid():N}@family.example",
            CanMessage = true,
            CanManageAppointments = true,
            CanRequestRefills = false,
            CanPayBills = false
        });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var code = doc.RootElement.GetProperty("code").GetString();

        Assert.NotNull(code);
        Assert.StartsWith("INV-", code);

        // 16 bytes = 32 hex chars -> "INV-" (4 chars) + 32 chars = 36 total length (128 bits entropy)
        var hexPart = code.Substring(4);
        Assert.Equal(32, hexPart.Length);
    }

    [Fact]
    public async Task ProxyInvite_Acceptance_ByDifferentEmail_RejectedWithBadRequest()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var inviter = new Person { FirstName = "Inviter", LastName = "User", Email = $"inviter.{Guid.NewGuid():N}@patient.example", DateOfBirth = DateTime.UtcNow.AddYears(-30) };
        db.Persons.Add(inviter);

        var intendedEmail = $"intended.{Guid.NewGuid():N}@family.example";
        var plainCode = $"INV-{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))}";
        var codeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainCode)));

        var invite = new ProxyInvite
        {
            Inviter = inviter,
            InviterPersonId = inviter.Id,
            Email = intendedEmail,
            CodeHash = codeHash,
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        db.ProxyInvites.Add(invite);

        // Attacker account with a different email
        var attacker = new Person { FirstName = "Attacker", LastName = "User", Email = $"attacker.{Guid.NewGuid():N}@intruder.example", DateOfBirth = DateTime.UtcNow.AddYears(-25) };
        var attackerAccount = new UserAccount { Person = attacker, Email = attacker.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.PATIENT, IsActive = true, AuthorizationVersion = 1 };
        db.Persons.Add(attacker);
        db.UserAccounts.Add(attackerAccount);
        await db.SaveChangesAsync();

        var attackerToken = db.CreateAuthenticatedSession(tokens, attackerAccount, attacker.Id);

        var client = _factory.CreateClient();

        // Attacker attempts to accept the invite -> 400 BadRequest
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/invites/accept");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", attackerToken);
        req.Content = JsonContent.Create(new { Code = plainCode });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var resJson = await res.Content.ReadAsStringAsync();
        Assert.Contains("different email address", resJson);
    }

    // =========================================================================
    // 7. SEC-15: FORM WRITES REQUIRE WRITE PERMISSION
    // =========================================================================

    [Fact]
    public async Task Forms_ProxyWithReadOnlyPermission_SubmittingForm_RejectedWithForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var patientPerson = new Person { FirstName = "Patient", LastName = "User", Email = $"patient.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-25) };
        var patient = new Patient { Person = patientPerson, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        db.Patients.Add(patient);

        var formDef = new FormDefinition { Title = "Intake Form", Slug = "intake-form", FieldsJson = "[]" };
        db.FormDefinitions.Add(formDef);

        var proxyPerson = new Person { FirstName = "Proxy", LastName = "ReadOnly", Email = $"proxy.{Guid.NewGuid():N}@clinic.example", DateOfBirth = DateTime.UtcNow.AddYears(-25) };
        var proxyAccount = new UserAccount { Person = proxyPerson, Email = proxyPerson.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.PATIENT, IsActive = true, AuthorizationVersion = 1 };
        db.UserAccounts.Add(proxyAccount);

        // Grant has CanViewRecords = true, but CanSubmitForms = false
        var grant = new ProxyGrant
        {
            GrantorId = patientPerson.Id,
            GranteeId = proxyPerson.Id,
            CanViewRecords = true,
            CanSubmitForms = false,
            ConsentGivenAt = DateTime.UtcNow
        };
        db.ProxyGrants.Add(grant);
        await db.SaveChangesAsync();

        var proxyToken = db.CreateAuthenticatedSession(tokens, proxyAccount, proxyPerson.Id);

        var client = _factory.CreateClient();

        // Read-only proxy attempts to submit form -> 403 Forbidden
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/forms/{formDef.Id}/submit");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", proxyToken);
        req.Headers.Add("x-patient-id", patient.Id.ToString());
        req.Content = JsonContent.Create(new { Answers = new Dictionary<string, object> { ["symptom"] = "fever" } });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // =========================================================================
    // 8. SEC-16: DIRECT PATIENT/PROXY REFUND TRIGGERING
    // =========================================================================

    [Fact]
    public async Task Refund_PatientOrProxy_AttemptingDirectRefund_RejectedWithForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var person = new Person { FirstName = "Refund", LastName = "User", Email = $"refund.{Guid.NewGuid():N}@patient.example", DateOfBirth = DateTime.UtcNow.AddYears(-30) };
        var patient = new Patient { Person = person, Mrn = $"MRN-{Guid.NewGuid():N}"[..10] };
        var account = new UserAccount { Person = person, Email = person.Email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.PATIENT, IsActive = true, AuthorizationVersion = 1 };
        var statement = new Statement { Patient = patient, TotalCents = 10000, BalanceCents = 0, Status = StatementStatus.PAID };
        var payment = new Payment { Statement = statement, AmountCents = 10000, Status = PaymentStatus.SUCCEEDED, PaymentToken = "pi_fake_123", GatewayPaymentIntentId = "pi_fake_123" };

        db.Patients.Add(patient);
        db.UserAccounts.Add(account);
        db.Statements.Add(statement);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);

        var client = _factory.CreateClient();

        // Patient attempting to trigger gateway refund -> 403 Forbidden
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/refunds");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("x-patient-id", patient.Id.ToString());
        req.Content = JsonContent.Create(new { PaymentId = payment.Id, AmountCents = 5000, Reason = "Patient self-refund" });

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var resJson = await res.Content.ReadAsStringAsync();
        Assert.Contains("restricted to authorized billing administrators", resJson);
    }
}
