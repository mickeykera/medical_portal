using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/hms/orders")]
[RequireAuth]
public class OrdersController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public OrdersController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("worklist")]
    [RequirePermission(AppResource.Orders, AppAction.Read)]
    public async Task<IActionResult> Worklist(
        [FromQuery] ClinicalOrderStatus? status = null,
        [FromQuery] OrderPriority? priority = null,
        [FromQuery] Guid? departmentId = null)
    {
        var query = _db.ClinicalOrders.AsNoTracking().Include(o => o.Patient).ThenInclude(p => p.Person).AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (priority.HasValue) query = query.Where(o => o.Priority == priority.Value);
        if (departmentId.HasValue) query = query.Where(o => o.PerformingDepartmentId == departmentId.Value);

        var orders = await query.OrderByDescending(o => o.Priority).ThenBy(o => o.CreatedAt).Take(200).ToListAsync();
        return Ok(new
        {
            orders = orders.Select(o => new
            {
                id = o.Id,
                patientId = o.PatientId,
                patientName = $"{o.Patient.Person.FirstName} {o.Patient.Person.LastName}",
                mrn = o.Patient.Mrn,
                type = o.Type.ToString(),
                priority = o.Priority.ToString(),
                status = o.Status.ToString(),
                paymentGate = o.PaymentGate.ToString(),
                description = o.Description,
                createdAt = o.CreatedAt
            })
        });
    }

    [HttpGet("tariffs")]
    [RequirePermission(AppResource.Billing, AppAction.Read)]
    public async Task<IActionResult> ListTariffs()
    {
        var now = DateTime.UtcNow;
        var tariffs = await _db.Tariffs.AsNoTracking()
            .Where(t => t.IsActive && t.EffectiveFrom <= now && (!t.EffectiveTo.HasValue || t.EffectiveTo > now))
            .OrderBy(t => t.Name)
            .ToListAsync();
        return Ok(new
        {
            tariffs = tariffs.Select(t => new
            {
                id = t.Id,
                code = t.Code,
                name = t.Name,
                orderType = t.OrderType?.ToString(),
                amountCents = t.AmountCents,
                currency = t.Currency,
                effectiveFrom = t.EffectiveFrom,
                effectiveTo = t.EffectiveTo
            })
        });
    }

    [HttpPost("tariffs")]
    [RequirePermission(AppResource.Billing, AppAction.Admin)]
    public async Task<IActionResult> CreateTariff([FromBody] CreateTariffRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Tariff code and name are required." });
        if (request.AmountCents <= 0)
            return BadRequest(new { error = "Tariff amount must be positive." });
        if (await _db.Tariffs.AnyAsync(t => t.Code == request.Code.Trim()))
            return Conflict(new { error = "Tariff code already exists." });

        var tariff = new Tariff
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            OrderType = request.OrderType,
            AmountCents = request.AmountCents,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "ETB" : request.Currency.Trim().ToUpperInvariant(),
            EffectiveFrom = request.EffectiveFrom ?? DateTime.UtcNow,
            EffectiveTo = request.EffectiveTo
        };
        _db.Tariffs.Add(tariff);
        await _db.SaveChangesAsync();
        return Created($"/api/billing/tariffs/{tariff.Id}", new { id = tariff.Id, code = tariff.Code });
    }

    [HttpPost]
    [RequirePermission(AppResource.Orders, AppAction.Write)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { error = "Order description is required." });

        if (!await _db.Patients.AnyAsync(p => p.Id == request.PatientId))
            return NotFound(new { error = "Patient not found." });

        var encounter = request.EncounterId.HasValue
            ? await _db.Encounters.FirstOrDefaultAsync(e => e.Id == request.EncounterId)
            : null;
        if (request.EncounterId.HasValue && encounter == null)
            return NotFound(new { error = "Encounter not found." });
        if (encounter != null && encounter.PatientId != request.PatientId)
            return BadRequest(new { error = "Encounter does not belong to the selected patient." });

        var emergency = encounter?.PatientClass == PatientClass.EMERGENCY;
        Tariff? tariff = null;
        if (request.TariffId.HasValue)
        {
            tariff = await _db.Tariffs.FirstOrDefaultAsync(t =>
                t.Id == request.TariffId.Value && t.IsActive &&
                t.EffectiveFrom <= DateTime.UtcNow &&
                (!t.EffectiveTo.HasValue || t.EffectiveTo > DateTime.UtcNow));
            if (tariff == null)
                return NotFound(new { error = "Tariff not found or inactive." });
            if (tariff.OrderType.HasValue && tariff.OrderType != request.Type)
                return BadRequest(new { error = "Tariff does not apply to this order type." });
        }

        var amountCents = tariff?.AmountCents ?? request.ChargeAmountCents.GetValueOrDefault();
        var chargeable = amountCents > 0;
        var order = new ClinicalOrder
        {
            PatientId = request.PatientId,
            EncounterId = request.EncounterId,
            OrderingUserAccountId = actor.UserAccountId,
            PerformingDepartmentId = request.PerformingDepartmentId,
            Type = request.Type,
            Priority = request.Priority,
            Description = request.Description.Trim(),
            ClinicalIndication = request.ClinicalIndication?.Trim(),
            ChargeAmountCents = amountCents > 0 ? amountCents : null,
            TariffId = tariff?.Id,
            PaymentGate = !chargeable ? PaymentGateState.NOT_REQUIRED :
                emergency ? PaymentGateState.BILL_LATER : PaymentGateState.AWAITING_PAYMENT,
            Status = !chargeable || emergency ? ClinicalOrderStatus.RELEASED : ClinicalOrderStatus.PENDING_PAYMENT,
            ReleasedAt = !chargeable || emergency ? DateTime.UtcNow : null
        };
        _db.ClinicalOrders.Add(order);
        if (chargeable)
        {
            _db.ChargeItems.Add(new ChargeItem
            {
                PatientId = order.PatientId,
                EncounterId = order.EncounterId,
                ClinicalOrder = order,
                TariffId = tariff?.Id,
                Description = tariff?.Name ?? "Clinical service",
                AmountCents = amountCents,
                Currency = tariff?.Currency ?? "ETB",
                Status = emergency ? ChargeItemStatus.OPEN : ChargeItemStatus.OPEN
            });
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ORDER_CREATED", "ClinicalOrder", order.Id.ToString(), actor.UserAccountId, order.PatientId,
            metadata: new { order.Type, order.Priority, order.PaymentGate });
        return Created($"/api/hms/orders/{order.Id}", new { id = order.Id, status = order.Status.ToString(), paymentGate = order.PaymentGate.ToString() });
    }

    [HttpPost("{id:guid}/release")]
    [RequirePermission(AppResource.Orders, AppAction.Admin)]
    public async Task<IActionResult> Release(Guid id, [FromBody] ReleaseOrderRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        var order = await _db.ClinicalOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound(new { error = "Order not found." });
        if (order.Status != ClinicalOrderStatus.PENDING_PAYMENT)
            return Conflict(new { error = "Only pending-payment orders can be released." });
        if (request.Waived && string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "A waiver reason is required." });

        var chargeItem = await _db.ChargeItems.FirstOrDefaultAsync(c => c.ClinicalOrderId == order.Id);
        order.PaymentGate = request.Waived ? PaymentGateState.WAIVED : PaymentGateState.PAID;
        order.GateExceptionReason = request.Reason?.Trim();
        order.GateApprovedByUserAccountId = actor.UserAccountId;
        order.Status = ClinicalOrderStatus.RELEASED;
        order.ReleasedAt = DateTime.UtcNow;
        if (chargeItem != null)
        {
            chargeItem.Status = request.Waived ? ChargeItemStatus.WAIVED : ChargeItemStatus.PAID;
            chargeItem.WaiverReason = request.Waived ? order.GateExceptionReason : null;
            chargeItem.WaivedByUserAccountId = request.Waived ? actor.UserAccountId : null;

            if (!request.Waived)
            {
                var statement = new Statement
                {
                    PatientId = order.PatientId,
                    TotalCents = chargeItem.AmountCents,
                    AmountPaidCents = chargeItem.AmountCents,
                    BalanceCents = 0,
                    Description = chargeItem.Description,
                    Status = StatementStatus.PAID
                };
                var payment = new Payment
                {
                    Statement = statement,
                    AmountCents = chargeItem.AmountCents,
                    PaymentToken = $"CASHIER:{actor.UserAccountId}",
                    Status = PaymentStatus.SUCCEEDED,
                    IdempotencyKey = $"ORDER:{order.Id}"
                };
                _db.Payments.Add(payment);
                _db.Receipts.Add(new Receipt
                {
                    PatientId = order.PatientId,
                    Payment = payment,
                    ReceiptNumber = $"R-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..29],
                    AmountCents = chargeItem.AmountCents
                });
            }
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(request.Waived ? "ORDER_WAIVED" : "ORDER_RELEASED", "ClinicalOrder", id.ToString(), actor.UserAccountId, order.PatientId,
            metadata: new { reason = order.GateExceptionReason });
        return Ok(new { id, status = order.Status.ToString(), paymentGate = order.PaymentGate.ToString() });
    }

    [HttpPost("{id:guid}/start")]
    [RequirePermission(AppResource.Orders, AppAction.Write)]
    public async Task<IActionResult> Start(Guid id)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        var order = await _db.ClinicalOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound(new { error = "Order not found." });
        if (order.Status != ClinicalOrderStatus.RELEASED)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Order is not released for service.", reason = "payment_gate" });
        order.Status = ClinicalOrderStatus.IN_PROGRESS;
        order.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ORDER_STARTED", "ClinicalOrder", id.ToString(), actor.UserAccountId, order.PatientId);
        return Ok(new { id, status = order.Status.ToString() });
    }

    public sealed record CreateOrderRequest(
        Guid PatientId,
        ClinicalOrderType Type,
        string Description,
        Guid? EncounterId = null,
        Guid? PerformingDepartmentId = null,
        OrderPriority Priority = OrderPriority.ROUTINE,
        string? ClinicalIndication = null,
        int? ChargeAmountCents = null,
        Guid? TariffId = null);

    public sealed record ReleaseOrderRequest(bool Waived = false, string? Reason = null);
    public sealed record CreateTariffRequest(
        string Code,
        string Name,
        int AmountCents,
        ClinicalOrderType? OrderType = null,
        string? Currency = "ETB",
        DateTime? EffectiveFrom = null,
        DateTime? EffectiveTo = null);
}
