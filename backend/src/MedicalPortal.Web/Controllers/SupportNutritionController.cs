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
[Route("api/support-services")]
[RequireAuth]
public class SupportNutritionController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public SupportNutritionController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("meal-list")]
    public async Task<IActionResult> MealList()
    {
        if (!CanManage(Actor())) return Forbid();
        var orders = await _db.DietOrders.AsNoTracking().Where(x => x.Status == DietOrderStatus.ACTIVE).OrderBy(x => x.CreatedAt).ToListAsync();
        return Ok(new { orders });
    }

    [HttpPost("diet-orders")]
    public async Task<IActionResult> CreateDietOrder([FromBody] CreateDietOrderRequest request)
    {
        var actor = Actor();
        if (!CanManage(actor)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.DietType) || request.PatientId == Guid.Empty || request.EncounterId == Guid.Empty)
            return BadRequest(new { error = "Patient, encounter, and diet type are required." });
        if (!await _db.Encounters.AnyAsync(x => x.Id == request.EncounterId && x.PatientId == request.PatientId && x.Status == EncounterStatus.ACTIVE))
            return NotFound(new { error = "Active patient encounter not found." });
        var order = new DietOrder
        {
            PatientId = request.PatientId, EncounterId = request.EncounterId, DietType = request.DietType.Trim(),
            Npo = request.Npo, AllergiesAndRestrictions = request.AllergiesAndRestrictions?.Trim(),
            OrderedByUserAccountId = actor.UserAccountId
        };
        _db.DietOrders.Add(order);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DIET_ORDER_CREATED", "DietOrder", order.Id.ToString(), actor.UserAccountId, order.PatientId);
        return Created($"/api/support-services/diet-orders/{order.Id}", new { id = order.Id, status = order.Status.ToString() });
    }

    [HttpPost("linen")]
    public async Task<IActionResult> RecordLinen([FromBody] LinenRequest request)
    {
        var actor = Actor();
        if (!CanManage(actor)) return Forbid();
        if (request.DepartmentId == Guid.Empty || string.IsNullOrWhiteSpace(request.LinenItem) || request.Quantity <= 0 || !Enum.IsDefined(request.Action))
            return BadRequest(new { error = "Department, linen item, and positive quantity are required." });
        var entry = new LinenLedgerEntry
        {
            DepartmentId = request.DepartmentId, LinenItem = request.LinenItem.Trim(),
            Action = request.Action, Quantity = request.Quantity, RecordedByUserAccountId = actor.UserAccountId
        };
        _db.LinenLedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("LINEN_LEDGER_RECORDED", "LinenLedgerEntry", entry.Id.ToString(), actor.UserAccountId,
            metadata: new { entry.DepartmentId, entry.LinenItem, entry.Action, entry.Quantity });
        return Ok(new { id = entry.Id });
    }

    private AccessTokenClaims Actor() => (AccessTokenClaims)HttpContext.Items["Actor"]!;
    private static bool CanManage(AccessTokenClaims actor) =>
        actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.DEPT_ADMIN or UserRole.NURSE;

    public sealed record CreateDietOrderRequest(Guid PatientId, Guid EncounterId, string DietType, bool Npo = false, string? AllergiesAndRestrictions = null);
    public sealed record LinenRequest(Guid DepartmentId, string LinenItem, LinenLedgerAction Action, int Quantity);
}
