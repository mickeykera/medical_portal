using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Payments;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedicalPortal.Tests;

public class PaymentGatewayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PaymentGatewayTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(UserAccount Account, Patient Patient, string Token)> CreateIsolatedPatientAsync(
        MedicalPortalDbContext db,
        ITokenService tokenService)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var person = new Person
        {
            FirstName = "PayTest",
            LastName = id,
            Email = $"patient.{id}@test.example",
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Persons.Add(person);

        var account = new UserAccount
        {
            Person = person,
            Email = person.Email,
            PasswordHash = "dummy_hash",
            Role = UserRole.PATIENT,
            IsActive = true
        };
        db.UserAccounts.Add(account);

        var patient = new Patient
        {
            Person = person,
            Mrn = "MRN_" + id
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateAccessToken(account, person.Id);
        return (account, patient, token);
    }

    [Fact]
    public void BillingLogic_FifoAllocation_AllocatesInCentsCorrectly()
    {
        var st1 = new Statement
        {
            Id = Guid.NewGuid(),
            TotalCents = 3000,
            AmountPaidCents = 0,
            BalanceCents = 3000,
            DueAt = DateTime.UtcNow.AddDays(5),
            Status = StatementStatus.OPEN
        };

        var st2 = new Statement
        {
            Id = Guid.NewGuid(),
            TotalCents = 4000,
            AmountPaidCents = 0,
            BalanceCents = 4000,
            DueAt = DateTime.UtcNow.AddDays(10),
            Status = StatementStatus.OPEN
        };

        var st3 = new Statement
        {
            Id = Guid.NewGuid(),
            TotalCents = 5000,
            AmountPaidCents = 0,
            BalanceCents = 5000,
            DueAt = DateTime.UtcNow.AddDays(15),
            Status = StatementStatus.OPEN
        };

        var statements = new[] { st3, st1, st2 }; // Intentionally unsorted

        // Pay $50.00 (5000 cents):
        // st1 ($30) should be fully paid (0 balance)
        // st2 ($40) should be partially paid ($20 paid, $20 balance)
        // st3 ($50) should be untouched ($50 balance)
        var allocations = BillingLogic.AllocatePaymentFifo(statements, 5000);

        Assert.Equal(2, allocations.Count);

        Assert.Equal(st1.Id, allocations[0].StatementId);
        Assert.Equal(3000, allocations[0].AllocatedCents);
        Assert.Equal(0, st1.BalanceCents);
        Assert.Equal(3000, st1.AmountPaidCents);
        Assert.Equal(StatementStatus.PAID, st1.Status);

        Assert.Equal(st2.Id, allocations[1].StatementId);
        Assert.Equal(2000, allocations[1].AllocatedCents);
        Assert.Equal(2000, st2.BalanceCents);
        Assert.Equal(2000, st2.AmountPaidCents);
        Assert.Equal(StatementStatus.PARTIALLY_PAID, st2.Status);

        Assert.Equal(5000, st3.BalanceCents);
        Assert.Equal(0, st3.AmountPaidCents);
        Assert.Equal(StatementStatus.OPEN, st3.Status);
    }

    [Fact]
    public void BillingLogic_RevertAllocation_RestoresBalanceAccurately()
    {
        var st = new Statement
        {
            TotalCents = 5000,
            AmountPaidCents = 5000,
            BalanceCents = 0,
            Status = StatementStatus.PAID
        };

        // Partial refund of $20.00 (2000 cents)
        BillingLogic.RevertAllocation(st, 2000);
        Assert.Equal(3000, st.AmountPaidCents);
        Assert.Equal(2000, st.BalanceCents);
        Assert.Equal(StatementStatus.PARTIALLY_PAID, st.Status);

        // Remaining refund of $30.00 (3000 cents)
        BillingLogic.RevertAllocation(st, 3000);
        Assert.Equal(0, st.AmountPaidCents);
        Assert.Equal(5000, st.BalanceCents);
        Assert.Equal(StatementStatus.OPEN, st.Status);
    }

    [Fact]
    public async Task FakePaymentGateway_SimulatesHostedFieldsAndLast4Properly()
    {
        var gateway = new FakePaymentGateway();
        gateway.DefaultLast4 = "7890";

        var intentRes = await gateway.CreatePaymentIntentAsync(new PaymentIntentRequest(
            AmountCents: 4500,
            Currency: "usd",
            PatientId: Guid.NewGuid()));

        Assert.True(intentRes.Success);
        Assert.StartsWith("pi_fake_", intentRes.PaymentIntentId);
        Assert.Contains(intentRes.PaymentIntentId, intentRes.ClientSecret);

        var confirmRes = await gateway.ConfirmPaymentAsync(new ConfirmPaymentRequest(
            PaymentIntentId: intentRes.PaymentIntentId,
            PaymentMethodToken: "pm_card_custom_1122"));

        Assert.True(confirmRes.Success);
        Assert.Equal("1122", confirmRes.Last4); // Extracted from token digits, NOT 4242!
        Assert.Equal("succeeded", confirmRes.Status);
    }

    [Fact]
    public async Task StripePaymentGateway_ReadsKeysFromConfigurationOnly()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var gateway = new StripePaymentGateway(new HttpClient(), emptyConfig, NullLogger<StripePaymentGateway>.Instance);

        // Secret key missing: throws InvalidOperationException on API call
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.CreatePaymentIntentAsync(new PaymentIntentRequest(1000)));

        Assert.Contains("secret key is not configured", ex.Message);

        // Webhook secret missing: returns failure with descriptive message
        var webhookRes = gateway.VerifyWebhook("{}", "t=123,v1=abc");
        Assert.False(webhookRes.IsValid);
        Assert.Contains("webhook secret is not configured", webhookRes.ErrorMessage);
    }

    [Fact]
    public void WebhookVerification_ValidatesSignaturesAndRejectsTampering()
    {
        var secret = "whsec_test_secret_for_security_checks_1234";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = secret
            })
            .Build();

        var gateway = new StripePaymentGateway(new HttpClient(), config, NullLogger<StripePaymentGateway>.Instance);

        var payload = "{\"id\":\"evt_test123\",\"type\":\"payment_intent.succeeded\"}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. Valid signature
        var validHeader = FakePaymentGateway.GenerateSignatureHeader(payload, secret, now);
        var resultValid = gateway.VerifyWebhook(payload, validHeader);
        Assert.True(resultValid.IsValid);
        Assert.Equal("evt_test123", resultValid.EventId);
        Assert.Equal("payment_intent.succeeded", resultValid.EventType);

        // 2. Tampered payload
        var tamperedPayload = "{\"id\":\"evt_test123\",\"type\":\"payment_intent.succeeded\",\"tampered\":true}";
        var resultTampered = gateway.VerifyWebhook(tamperedPayload, validHeader);
        Assert.False(resultTampered.IsValid);
        Assert.Contains("Invalid webhook signature", resultTampered.ErrorMessage);

        // 3. Expired timestamp (> 300 seconds ago)
        var oldHeader = FakePaymentGateway.GenerateSignatureHeader(payload, secret, now - 500);
        var resultExpired = gateway.VerifyWebhook(payload, oldHeader);
        Assert.False(resultExpired.IsValid);
        Assert.Contains("outside tolerance window", resultExpired.ErrorMessage);
    }

    [Fact]
    public async Task Api_PaymentIntent_HostedFieldsFlow_Succeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var (_, patient, token) = await CreateIsolatedPatientAsync(db, tokenService);

        // Add open statement so patient has balance
        db.Statements.Add(new Statement
        {
            PatientId = patient.Id,
            TotalCents = 5000,
            BalanceCents = 5000,
            AmountPaidCents = 0,
            Description = "Diagnostic Work",
            DueAt = DateTime.UtcNow.AddDays(7),
            Status = StatementStatus.OPEN
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Patient-Id", patient.Id.ToString());

        var response = await client.PostAsJsonAsync("/api/billing/payment-intent", new
        {
            amount = "25.00"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.TryGetProperty("paymentIntentId", out var piProp));
        Assert.True(doc.TryGetProperty("clientSecret", out var csProp));
        Assert.Equal(2500, doc.GetProperty("amountCents").GetInt32());
        Assert.StartsWith("pi_", piProp.GetString()!);
        Assert.Contains(piProp.GetString()!, csProp.GetString()!);
    }

    [Fact]
    public async Task Api_SubmitPayment_HostedFields_AppliesFifoAndStoresGatewayMetadata()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();

        var (_, patient, token) = await CreateIsolatedPatientAsync(db, tokenService);

        // Create isolated test statements
        var st1 = new Statement
        {
            PatientId = patient.Id,
            TotalCents = 2500,
            BalanceCents = 2500,
            AmountPaidCents = 0,
            Description = "Lab work",
            DueAt = DateTime.UtcNow.AddDays(3),
            Status = StatementStatus.OPEN
        };
        var st2 = new Statement
        {
            PatientId = patient.Id,
            TotalCents = 5000,
            BalanceCents = 5000,
            AmountPaidCents = 0,
            Description = "Office visit",
            DueAt = DateTime.UtcNow.AddDays(7),
            Status = StatementStatus.OPEN
        };
        db.Statements.AddRange(st1, st2);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Patient-Id", patient.Id.ToString());

        // 1. Create PaymentIntent via hosted fields API
        var intent = await gateway.CreatePaymentIntentAsync(new PaymentIntentRequest(
            AmountCents: 4000,
            PatientId: patient.Id));

        // Confirm with a custom test token ending in 3344 (NOT 4242!)
        await gateway.ConfirmPaymentAsync(new ConfirmPaymentRequest(
            PaymentIntentId: intent.PaymentIntentId,
            PaymentMethodToken: "pm_card_test_3344"));

        // 2. Submit payment confirmation to portal
        var paymentRes = await client.PostAsJsonAsync("/api/billing/payments", new
        {
            paymentIntentId = intent.PaymentIntentId
        });

        Assert.Equal(HttpStatusCode.Created, paymentRes.StatusCode);

        var doc = await paymentRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4000, doc.GetProperty("paid").GetInt32());
        Assert.Equal("3344", doc.GetProperty("last4").GetString()); // Gateway Last4 verified!

        // Check statement balances in DB
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var reloadedSt1 = await checkDb.Statements.FirstAsync(s => s.Id == st1.Id);
        var reloadedSt2 = await checkDb.Statements.FirstAsync(s => s.Id == st2.Id);

        // st1: $25 fully paid (0 balance)
        Assert.Equal(0, reloadedSt1.BalanceCents);
        Assert.Equal(2500, reloadedSt1.AmountPaidCents);
        Assert.Equal(StatementStatus.PAID, reloadedSt1.Status);

        // st2: $15 paid of $50 ($35 balance remaining)
        Assert.Equal(3500, reloadedSt2.BalanceCents);
        Assert.Equal(1500, reloadedSt2.AmountPaidCents);
        Assert.Equal(StatementStatus.PARTIALLY_PAID, reloadedSt2.Status);

        // Verify Payment records stored in DB
        var payments = await checkDb.Payments
            .Where(p => p.GatewayPaymentIntentId == intent.PaymentIntentId)
            .ToListAsync();

        Assert.NotEmpty(payments);
        Assert.All(payments, p =>
        {
            Assert.Equal("3344", p.Last4);
            Assert.Equal(PaymentStatus.SUCCEEDED, p.Status);
            Assert.Equal(intent.PaymentIntentId, p.GatewayPaymentIntentId);
            Assert.NotNull(p.GatewayChargeId);
        });
    }

    [Fact]
    public async Task Api_SubmitPayment_EnforcesIdempotencyKeys()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var (_, patient, token) = await CreateIsolatedPatientAsync(db, tokenService);

        var statement = new Statement
        {
            PatientId = patient.Id,
            TotalCents = 10000,
            BalanceCents = 10000,
            AmountPaidCents = 0,
            Description = "Specialist consultation",
            DueAt = DateTime.UtcNow.AddDays(5),
            Status = StatementStatus.OPEN
        };
        db.Statements.Add(statement);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Patient-Id", patient.Id.ToString());

        var idempotencyKey = "idem_" + Guid.NewGuid().ToString("N");

        var payload = new
        {
            amount = "30.00",
            paymentToken = "pm_card_9988",
            idempotencyKey = idempotencyKey
        };

        // First call -> 201 Created
        var res1 = await client.PostAsJsonAsync("/api/billing/payments", payload);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Second call with same idempotency key -> 200 OK (idempotent replay, no double charge)
        var res2 = await client.PostAsJsonAsync("/api/billing/payments", payload);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var doc = await res2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("isDuplicate").GetBoolean());
        Assert.Equal(3000, doc.GetProperty("paid").GetInt32());

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var stReloaded = await verifyDb.Statements.FirstAsync(s => s.Id == statement.Id);
        // Statement balance must only be reduced once by $30 (7000 balance remaining, NOT 4000!)
        Assert.Equal(7000, stReloaded.BalanceCents);
        Assert.Equal(3000, stReloaded.AmountPaidCents);

        var paymentCount = await verifyDb.Payments
            .CountAsync(p => p.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, paymentCount);
    }

    [Fact]
    public async Task Api_Webhook_PaymentSucceededAndDuplicateHandling()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var gateway = (FakePaymentGateway)scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var (_, patient, _) = await CreateIsolatedPatientAsync(db, tokenService);

        var statement = new Statement
        {
            PatientId = patient.Id,
            TotalCents = 6000,
            BalanceCents = 6000,
            AmountPaidCents = 0,
            Description = "Diagnostic Ultrasound",
            DueAt = DateTime.UtcNow.AddDays(4),
            Status = StatementStatus.OPEN
        };
        db.Statements.Add(statement);
        await db.SaveChangesAsync();

        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var paymentIntentId = "pi_webhook_" + Guid.NewGuid().ToString("N");
        var chargeId = "ch_webhook_" + Guid.NewGuid().ToString("N");

        var payload = FakePaymentGateway.CreateWebhookPayload(
            eventId: eventId,
            eventType: "payment_intent.succeeded",
            paymentIntentId: paymentIntentId,
            amountCents: 6000,
            patientId: patient.Id,
            last4: "8877",
            chargeId: chargeId);

        var secret = gateway.GetWebhookSecret();
        var signature = FakePaymentGateway.GenerateSignatureHeader(payload, secret);

        var client = _factory.CreateClient();
        using var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook");
        request1.Headers.Add("Stripe-Signature", signature);
        request1.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response1 = await client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Verify statement is paid
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var reloaded = await verifyDb.Statements.FirstAsync(s => s.Id == statement.Id);
        Assert.Equal(0, reloaded.BalanceCents);
        Assert.Equal(StatementStatus.PAID, reloaded.Status);

        // Send same webhook event again -> Idempotent response (duplicate = true)
        using var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook");
        request2.Headers.Add("Stripe-Signature", signature);
        request2.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response2 = await client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var doc = await response2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("duplicate").GetBoolean());
    }

    [Fact]
    public async Task Api_Webhook_RefundRestoresStatementBalance()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var gateway = (FakePaymentGateway)scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var (_, patient, _) = await CreateIsolatedPatientAsync(db, tokenService);

        var statement = new Statement
        {
            PatientId = patient.Id,
            TotalCents = 5000,
            BalanceCents = 0,
            AmountPaidCents = 5000,
            Description = "Physical Therapy",
            Status = StatementStatus.PAID
        };
        db.Statements.Add(statement);

        var piId = "pi_refund_test_" + Guid.NewGuid().ToString("N");
        var chId = "ch_refund_test_" + Guid.NewGuid().ToString("N");

        var payment = new Payment
        {
            Statement = statement,
            AmountCents = 5000,
            PaymentToken = piId,
            GatewayPaymentIntentId = piId,
            GatewayChargeId = chId,
            Status = PaymentStatus.SUCCEEDED,
            Last4 = "5566"
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // Send refund webhook
        var refundId = "re_" + Guid.NewGuid().ToString("N");
        var eventId = "evt_" + Guid.NewGuid().ToString("N");

        var payload = FakePaymentGateway.CreateWebhookPayload(
            eventId: eventId,
            eventType: "charge.refunded",
            paymentIntentId: piId,
            amountCents: 5000,
            patientId: patient.Id,
            last4: "5566",
            chargeId: chId,
            refundId: refundId);

        var secret = gateway.GetWebhookSecret();
        var signature = FakePaymentGateway.GenerateSignatureHeader(payload, secret);

        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook");
        request.Headers.Add("Stripe-Signature", signature);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify payment is REFUNDED and statement balance is restored
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var reloadedStmt = await verifyDb.Statements.FirstAsync(s => s.Id == statement.Id);
        Assert.Equal(5000, reloadedStmt.BalanceCents);
        Assert.Equal(0, reloadedStmt.AmountPaidCents);
        Assert.Equal(StatementStatus.OPEN, reloadedStmt.Status);

        var reloadedPayment = await verifyDb.Payments.FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.REFUNDED, reloadedPayment.Status);
        Assert.Equal(refundId, reloadedPayment.GatewayRefundId);
    }
}
