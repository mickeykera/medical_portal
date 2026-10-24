using System.Text;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Notifications;
using MedicalPortal.Infrastructure.Observability;
using MedicalPortal.Infrastructure.Payments;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/billing")]
[RequireAuth]
[EnableRateLimiting("GeneralApiPolicy")]
public class BillingController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly ISnapshotService _snapshotService;
    private readonly IAuditService _audit;
    private readonly IPaymentGateway _paymentGateway;
    private readonly INotificationDeliveryService? _deliveryService;

    public BillingController(
        MedicalPortalDbContext db,
        ISnapshotService snapshotService,
        IAuditService audit,
        IPaymentGateway paymentGateway,
        INotificationDeliveryService? deliveryService = null)
    {
        _db = db;
        _snapshotService = snapshotService;
        _audit = audit;
        _paymentGateway = paymentGateway;
        _deliveryService = deliveryService;
    }

    private Guid TargetPatientId => (Guid)HttpContext.Items["TargetPatientId"]!;
    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;

    [HttpGet("statements")]
    [RequirePatientAccess(Permission.payBills, AppResource.Billing, AppAction.Read)]
    public async Task<IActionResult> GetStatements()
    {
        var statements = await _db.Statements
            .AsNoTracking()
            .Include(s => s.Payments)
            .Where(s => s.PatientId == TargetPatientId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var totalOwed = statements.Where(s => s.Status != StatementStatus.PAID && s.Status != StatementStatus.VOID).Sum(s => s.BalanceCents);

        return Ok(new
        {
            statements = statements.Select(s => new
            {
                id = s.Id,
                description = s.Description,
                totalCents = s.TotalCents,
                balanceCents = s.BalanceCents,
                formatted = new
                {
                    total = ClinicalFormatting.FormatCents(s.TotalCents),
                    paid = ClinicalFormatting.FormatCents(s.AmountPaidCents),
                    balance = ClinicalFormatting.FormatCents(s.BalanceCents)
                },
                dueAt = s.DueAt?.ToString("o"),
                status = s.Status.ToString(),
                isOverdue = s.DueAt.HasValue && s.DueAt.Value < DateTime.UtcNow && s.BalanceCents > 0,
                payments = s.Payments.Select(p => new
                {
                    id = p.Id,
                    formatted = ClinicalFormatting.FormatCents(p.AmountCents),
                    amountCents = p.AmountCents,
                    status = p.Status.ToString(),
                    last4 = p.Last4,
                    cardBrand = p.CardBrand,
                    gatewayPaymentIntentId = p.GatewayPaymentIntentId,
                    createdAt = p.CreatedAt.ToString("o")
                })
            }),
            totalOwedCents = totalOwed,
            totalOwedFormatted = ClinicalFormatting.FormatCents(totalOwed)
        });
    }

    public record CreatePaymentIntentBody(string Amount, string? IdempotencyKey = null);

    [HttpPost("payment-intent")]
    [RequirePatientAccess(Permission.payBills, AppResource.Billing, AppAction.Write)]
    public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentBody body)
    {
        int amountCents;
        try
        {
            amountCents = BillingLogic.ParseMoneyToCents(body.Amount);
        }
        catch (BillingException ex)
        {
            return BadRequest(new { error = ex.Message, reason = ex.Code });
        }

        var hasBalance = await _db.Statements
            .AnyAsync(s => s.PatientId == TargetPatientId && s.BalanceCents > 0 && s.Status != StatementStatus.PAID && s.Status != StatementStatus.VOID);

        if (!hasBalance)
            return BadRequest(new { error = "No outstanding balance to pay." });

        var intent = await _paymentGateway.CreatePaymentIntentAsync(new PaymentIntentRequest(
            AmountCents: amountCents,
            Currency: "usd",
            PatientId: TargetPatientId,
            Description: $"Payment for patient {TargetPatientId}",
            IdempotencyKey: body.IdempotencyKey,
            Metadata: new Dictionary<string, string>
            {
                ["patientId"] = TargetPatientId.ToString(),
                ["actorAccountId"] = Actor.UserAccountId.ToString()
            }));

        if (!intent.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = intent.ErrorMessage ?? "Failed to create payment intent." });
        }

        return Ok(new
        {
            paymentIntentId = intent.PaymentIntentId,
            clientSecret = intent.ClientSecret,
            amountCents = intent.AmountCents,
            currency = intent.Currency
        });
    }

    public record PaymentBody(
        string? Amount = null,
        string? PaymentToken = null,
        string? PaymentIntentId = null,
        string? IdempotencyKey = null);

    [HttpPost("payments")]
    [RequirePatientAccess(Permission.payBills, AppResource.Billing, AppAction.Write)]
    public async Task<IActionResult> SubmitPayment([FromBody] PaymentBody body)
    {
        // Enforce idempotency if key provided
        if (!string.IsNullOrWhiteSpace(body.IdempotencyKey))
        {
            var existingPayments = await _db.Payments
                .Where(p => p.IdempotencyKey == body.IdempotencyKey)
                .ToListAsync();

            if (existingPayments.Count > 0)
            {
                var totalPaid = existingPayments.Sum(p => p.AmountCents);
                return Ok(new
                {
                    paid = totalPaid,
                    appliedTo = existingPayments.Select(p => new { statementId = p.StatementId, amountCents = p.AmountCents }),
                    last4 = existingPayments.FirstOrDefault()?.Last4,
                    message = "Payment already processed.",
                    isDuplicate = true
                });
            }
        }

        PaymentResult paymentResult;

        if (!string.IsNullOrWhiteSpace(body.PaymentIntentId))
        {
            // Flow 1: Hosted fields with PCI SAQ A scope (client confirmed via Stripe Elements)
            paymentResult = await _paymentGateway.GetPaymentDetailsAsync(body.PaymentIntentId);
            if (!paymentResult.Success || paymentResult.Status != "succeeded")
            {
                return BadRequest(new { error = paymentResult.ErrorMessage ?? "Payment has not succeeded.", reason = paymentResult.FailureReason });
            }
        }
        else if (!string.IsNullOrWhiteSpace(body.PaymentToken) && !string.IsNullOrWhiteSpace(body.Amount))
        {
            // Flow 2: Tokenized confirmation through gateway
            int amountCents;
            try
            {
                amountCents = BillingLogic.ParseMoneyToCents(body.Amount);
            }
            catch (BillingException ex)
            {
                return BadRequest(new { error = ex.Message, reason = ex.Code });
            }

            var intent = await _paymentGateway.CreatePaymentIntentAsync(new PaymentIntentRequest(
                AmountCents: amountCents,
                PatientId: TargetPatientId,
                IdempotencyKey: body.IdempotencyKey));

            if (!intent.Success)
                return StatusCode(StatusCodes.Status502BadGateway, new { error = intent.ErrorMessage });

            paymentResult = await _paymentGateway.ConfirmPaymentAsync(new ConfirmPaymentRequest(
                PaymentIntentId: intent.PaymentIntentId,
                PaymentMethodToken: body.PaymentToken,
                IdempotencyKey: body.IdempotencyKey));

            if (!paymentResult.Success)
                return BadRequest(new { error = paymentResult.ErrorMessage ?? "Payment failed.", reason = paymentResult.FailureReason });
        }
        else
        {
            return BadRequest(new { error = "Either PaymentIntentId or Amount and PaymentToken must be provided." });
        }

        var openStatements = await _db.Statements
            .Where(s => s.PatientId == TargetPatientId && s.BalanceCents > 0 && s.Status != StatementStatus.PAID && s.Status != StatementStatus.VOID)
            .OrderBy(s => s.DueAt ?? s.CreatedAt)
            .ToListAsync();

        if (openStatements.Count == 0)
            return BadRequest(new { error = "No outstanding balance to pay." });

        // Maintain strict FIFO allocation in cents
        var allocations = BillingLogic.AllocatePaymentFifo(openStatements, paymentResult.AmountCents);

        foreach (var alloc in allocations)
        {
            var payment = new Payment
            {
                StatementId = alloc.StatementId,
                AmountCents = alloc.AllocatedCents,
                PaymentToken = body.PaymentToken ?? paymentResult.PaymentIntentId,
                GatewayPaymentIntentId = paymentResult.PaymentIntentId,
                GatewayChargeId = paymentResult.ChargeId,
                Status = PaymentStatus.SUCCEEDED,
                Last4 = paymentResult.Last4,
                CardBrand = paymentResult.CardBrand,
                IdempotencyKey = body.IdempotencyKey,
                CreatedAt = DateTime.UtcNow
            };
            _db.Payments.Add(payment);
        }

        await _db.SaveChangesAsync();

        using (var activity = MedicalPortalDiagnostics.ActivitySource.StartActivity("portal.billing.payment"))
        {
            activity?.SetTag("payment.amount_cents", paymentResult.AmountCents);
            activity?.SetTag("payment.intent_id", paymentResult.PaymentIntentId);
            MedicalPortalDiagnostics.PaymentsProcessedCents.Add(paymentResult.AmountCents);
        }

        await _audit.LogAsync("PAYMENT_SUBMITTED", "Payment", paymentResult.PaymentIntentId, Actor.UserAccountId, TargetPatientId, metadata: new { amountCents = paymentResult.AmountCents, last4 = paymentResult.Last4 });
        await _snapshotService.RefreshSnapshotAsync(TargetPatientId);

        if (_deliveryService != null)
        {
            var person = await _db.Patients
                .Where(p => p.Id == TargetPatientId)
                .Select(p => p.Person)
                .FirstOrDefaultAsync();

            if (person != null)
            {
                try
                {
                    await _deliveryService.SendPaymentReceiptAsync(person, TargetPatientId, paymentResult.AmountCents, paymentResult.PaymentIntentId);
                }
                catch
                {
                    // Ignore delivery failure in fire-and-forget manner
                }
            }
        }

        return StatusCode(201, new
        {
            paid = paymentResult.AmountCents,
            appliedTo = allocations.Select(a => new { statementId = a.StatementId, amountCents = a.AllocatedCents }),
            last4 = paymentResult.Last4,
            message = "Payment received. Thank you."
        });
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleWebhook()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawPayload = await reader.ReadToEndAsync();

        var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();
        var verifyResult = _paymentGateway.VerifyWebhook(rawPayload, signatureHeader);

        if (!verifyResult.IsValid)
        {
            return BadRequest(new { error = verifyResult.ErrorMessage ?? "Invalid webhook signature." });
        }

        // Idempotency: skip already processed events
        if (!string.IsNullOrWhiteSpace(verifyResult.EventId))
        {
            var alreadyProcessed = await _db.ProcessedWebhookEvents
                .AnyAsync(e => e.EventId == verifyResult.EventId);
            if (alreadyProcessed)
            {
                return Ok(new { received = true, duplicate = true });
            }
        }

        if (verifyResult.EventType is "payment_intent.succeeded" or "charge.succeeded")
        {
            if (!string.IsNullOrWhiteSpace(verifyResult.PaymentIntentId))
            {
                var existingPayments = await _db.Payments
                    .Where(p => p.GatewayPaymentIntentId == verifyResult.PaymentIntentId)
                    .ToListAsync();

                if (existingPayments.Count > 0)
                {
                    foreach (var p in existingPayments)
                    {
                        p.Status = PaymentStatus.SUCCEEDED;
                        if (!string.IsNullOrEmpty(verifyResult.ChargeId)) p.GatewayChargeId = verifyResult.ChargeId;
                        if (!string.IsNullOrEmpty(verifyResult.Last4)) p.Last4 = verifyResult.Last4;
                    }
                }
                else if (verifyResult.PatientId.HasValue && verifyResult.AmountCents > 0)
                {
                    // Webhook arrived before client submission: apply FIFO allocation
                    var openStatements = await _db.Statements
                        .Where(s => s.PatientId == verifyResult.PatientId.Value && s.BalanceCents > 0 && s.Status != StatementStatus.PAID && s.Status != StatementStatus.VOID)
                        .OrderBy(s => s.DueAt ?? s.CreatedAt)
                        .ToListAsync();

                    if (openStatements.Count > 0)
                    {
                        var allocations = BillingLogic.AllocatePaymentFifo(openStatements, verifyResult.AmountCents);
                        foreach (var alloc in allocations)
                        {
                            _db.Payments.Add(new Payment
                            {
                                StatementId = alloc.StatementId,
                                AmountCents = alloc.AllocatedCents,
                                PaymentToken = verifyResult.PaymentIntentId,
                                GatewayPaymentIntentId = verifyResult.PaymentIntentId,
                                GatewayChargeId = verifyResult.ChargeId,
                                Status = PaymentStatus.SUCCEEDED,
                                Last4 = verifyResult.Last4,
                                CardBrand = verifyResult.CardBrand,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }

                await _audit.LogAsync("PAYMENT_SUCCEEDED", "Payment", verifyResult.PaymentIntentId, null, verifyResult.PatientId, metadata: new { amountCents = verifyResult.AmountCents, last4 = verifyResult.Last4 });
            }
        }
        else if (verifyResult.EventType is "payment_intent.payment_failed" or "charge.failed")
        {
            if (!string.IsNullOrWhiteSpace(verifyResult.PaymentIntentId))
            {
                var existingPayments = await _db.Payments
                    .Where(p => p.GatewayPaymentIntentId == verifyResult.PaymentIntentId)
                    .ToListAsync();

                foreach (var p in existingPayments)
                {
                    p.Status = PaymentStatus.FAILED;
                    p.FailureReason = verifyResult.FailureReason;
                }

                await _audit.LogAsync("PAYMENT_FAILED", "Payment", verifyResult.PaymentIntentId, null, verifyResult.PatientId, metadata: new { reason = verifyResult.FailureReason });

                if (verifyResult.PatientId.HasValue && _deliveryService != null)
                {
                    var person = await _db.Patients
                        .Where(p => p.Id == verifyResult.PatientId.Value)
                        .Select(p => p.Person)
                        .FirstOrDefaultAsync();

                    if (person != null)
                    {
                        try
                        {
                            await _deliveryService.SendBillingNoticeAsync(person, verifyResult.PatientId.Value, verifyResult.AmountCents, DateTime.UtcNow, verifyResult.PaymentIntentId);
                        }
                        catch
                        {
                            // Ignore failure
                        }
                    }
                }
            }
        }
        else if (verifyResult.EventType is "charge.refunded" or "payment_intent.refunded")
        {
            var targetPaymentId = verifyResult.PaymentIntentId;
            var existingPayments = await _db.Payments
                .Include(p => p.Statement)
                .Where(p => p.GatewayPaymentIntentId == targetPaymentId || (verifyResult.ChargeId != null && p.GatewayChargeId == verifyResult.ChargeId))
                .ToListAsync();

            foreach (var p in existingPayments)
            {
                p.Status = PaymentStatus.REFUNDED;
                p.GatewayRefundId = verifyResult.RefundId;

                // Revert statement FIFO allocation in cents
                if (p.Statement != null)
                {
                    BillingLogic.RevertAllocation(p.Statement, p.AmountCents);
                }
            }

            await _audit.LogAsync("PAYMENT_REFUNDED", "Payment", verifyResult.RefundId ?? targetPaymentId, null, verifyResult.PatientId, metadata: new { amountCents = verifyResult.AmountCents, refundId = verifyResult.RefundId });
        }

        if (!string.IsNullOrWhiteSpace(verifyResult.EventId))
        {
            _db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
            {
                EventId = verifyResult.EventId,
                EventType = verifyResult.EventType ?? "unknown",
                ProcessedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { received = true });
    }

    public record RefundBody(Guid PaymentId, int? AmountCents = null, string? Reason = null, string? IdempotencyKey = null);

    [HttpPost("refunds")]
    [RequirePatientAccess(Permission.payBills, AppResource.Billing, AppAction.Write)]
    public async Task<IActionResult> RefundPayment([FromBody] RefundBody body)
    {
        var payment = await _db.Payments
            .Include(p => p.Statement)
            .FirstOrDefaultAsync(p => p.Id == body.PaymentId && p.Statement.PatientId == TargetPatientId);

        if (payment == null)
            return NotFound(new { error = "Payment not found." });

        if (payment.Status != PaymentStatus.SUCCEEDED)
            return BadRequest(new { error = "Only succeeded payments can be refunded." });

        var refundAmount = body.AmountCents ?? payment.AmountCents;
        if (refundAmount <= 0 || refundAmount > payment.AmountCents)
            return BadRequest(new { error = "Invalid refund amount." });

        var refundResult = await _paymentGateway.RefundPaymentAsync(new RefundRequest(
            PaymentIntentId: payment.GatewayPaymentIntentId ?? payment.PaymentToken,
            ChargeId: payment.GatewayChargeId,
            AmountCents: refundAmount,
            Reason: body.Reason,
            IdempotencyKey: body.IdempotencyKey));

        if (!refundResult.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = refundResult.ErrorMessage ?? "Refund failed." });
        }

        payment.Status = PaymentStatus.REFUNDED;
        payment.GatewayRefundId = refundResult.RefundId;

        BillingLogic.RevertAllocation(payment.Statement, refundAmount);

        await _db.SaveChangesAsync();
        await _audit.LogAsync("PAYMENT_REFUNDED", "Payment", payment.Id.ToString(), Actor.UserAccountId, TargetPatientId, metadata: new { refundAmount, refundId = refundResult.RefundId });
        await _snapshotService.RefreshSnapshotAsync(TargetPatientId);

        return Ok(new
        {
            refundId = refundResult.RefundId,
            refundedAmountCents = refundAmount,
            status = "REFUNDED",
            message = "Refund processed successfully."
        });
    }

    [HttpGet("plans")]
    [RequirePatientAccess(Permission.payBills, AppResource.Billing, AppAction.Read)]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _db.PaymentPlans
            .AsNoTracking()
            .Include(p => p.Installments)
            .Where(p => p.PatientId == TargetPatientId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return Ok(new
        {
            plans = plans.Select(plan =>
            {
                var next = plan.Installments.Where(i => i.PaidAt == null).OrderBy(i => i.Sequence).FirstOrDefault();
                return new
                {
                    id = plan.Id,
                    statementId = plan.StatementId,
                    status = plan.Status.ToString(),
                    totalCents = plan.TotalCents,
                    installmentCents = plan.InstallmentCents,
                    numberOfPayments = plan.NumberOfPayments,
                    createdAt = plan.CreatedAt.ToString("o"),
                    nextInstallment = next != null ? new
                    {
                        sequence = next.Sequence,
                        amountCents = next.AmountCents,
                        dueAt = next.DueAt.ToString("o"),
                        isOverdue = next.DueAt < DateTime.UtcNow
                    } : null,
                    installments = plan.Installments.OrderBy(i => i.Sequence).Select(i => new
                    {
                        sequence = i.Sequence,
                        amountCents = i.AmountCents,
                        dueAt = i.DueAt.ToString("o"),
                        paidAt = i.PaidAt?.ToString("o")
                    })
                };
            })
        });
    }

    public record CreatePlanBody(string StatementId, int NumberOfPayments, string StartDate);

    [HttpPost("plans")]
    [RequirePatientAccess(Permission.payBills, AppResource.Billing, AppAction.Write)]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanBody body)
    {
        if (!Guid.TryParse(body.StatementId, out var statementId))
            return BadRequest(new { error = "Valid statement ID is required." });

        if (!DateTime.TryParse(body.StartDate, out var startDate))
            return BadRequest(new { error = "Valid start date is required." });

        var statement = await _db.Statements
            .FirstOrDefaultAsync(s => s.Id == statementId && s.PatientId == TargetPatientId);

        if (statement == null)
            return NotFound(new { error = "Statement not found." });

        var existingActive = await _db.PaymentPlans
            .AnyAsync(p => p.StatementId == statementId && p.Status == PaymentPlanStatus.ACTIVE);

        try
        {
            BillingLogic.AssertPlanAllowed(statement.BalanceCents, body.NumberOfPayments, existingActive);
        }
        catch (BillingException ex)
        {
            return BadRequest(new { error = ex.Message, reason = ex.Code });
        }

        var schedule = BillingLogic.BuildSchedule(statement.BalanceCents, body.NumberOfPayments, DateTime.SpecifyKind(startDate, DateTimeKind.Utc));

        var plan = new PaymentPlan
        {
            PatientId = TargetPatientId,
            StatementId = statement.Id,
            TotalCents = statement.BalanceCents,
            InstallmentCents = schedule.FirstOrDefault()?.AmountCents ?? statement.BalanceCents,
            NumberOfPayments = body.NumberOfPayments,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            Status = PaymentPlanStatus.ACTIVE,
            CreatedByUserAccountId = Actor.UserAccountId,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var s in schedule)
        {
            plan.Installments.Add(new PaymentInstallment
            {
                Plan = plan,
                Sequence = s.Sequence,
                AmountCents = s.AmountCents,
                DueAt = s.DueAt
            });
        }

        _db.PaymentPlans.Add(plan);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("PAYMENT_PLAN_CREATED", "PaymentPlan", plan.Id.ToString(), Actor.UserAccountId, TargetPatientId);

        return StatusCode(201, new
        {
            plan = new
            {
                id = plan.Id,
                statementId = plan.StatementId,
                numberOfPayments = plan.NumberOfPayments,
                installmentCents = plan.InstallmentCents,
                totalCents = plan.TotalCents,
                installments = plan.Installments.OrderBy(i => i.Sequence).Select(i => new
                {
                    sequence = i.Sequence,
                    amountCents = i.AmountCents,
                    dueAt = i.DueAt.ToString("o")
                })
            },
            message = $"Payment plan set up: {plan.NumberOfPayments} monthly payments."
        });
    }
}
