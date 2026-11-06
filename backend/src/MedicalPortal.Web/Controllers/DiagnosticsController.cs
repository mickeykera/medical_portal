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
[Route("api/diagnostics")]
[RequireAuth]
public class DiagnosticsController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public DiagnosticsController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("worklist")]
    public async Task<IActionResult> Worklist([FromQuery] DiagnosticService? service = null)
    {
        var actor = Actor();
        if (!CanAccess(actor, service)) return Forbid();
        var query = _db.DiagnosticWorkItems.AsNoTracking().Include(x => x.Patient).ThenInclude(x => x.Person)
            .Include(x => x.ClinicalOrder).Where(x => x.Status != DiagnosticWorkStatus.FINAL && x.Status != DiagnosticWorkStatus.CANCELLED);
        if (service.HasValue) query = query.Where(x => x.Service == service.Value);
        var items = await query.OrderByDescending(x => x.ClinicalOrder.Priority).ThenBy(x => x.CreatedAt).ToListAsync();
        return Ok(new
        {
            items = items.Select(x => new
            {
                id = x.Id, patientId = x.PatientId, name = $"{x.Patient.Person.FirstName} {x.Patient.Person.LastName}",
                mrn = x.Patient.Mrn, service = x.Service.ToString(), status = x.Status.ToString(),
                priority = x.ClinicalOrder.Priority.ToString(), accessionNumber = x.AccessionNumber,
                modality = x.Modality, createdAt = x.CreatedAt
            })
        });
    }

    [HttpPost("work-items")]
    public async Task<IActionResult> Create([FromBody] CreateDiagnosticRequest request)
    {
        var actor = Actor();
        if (!CanAccess(actor, request.Service)) return Forbid();
        if (request.Service == DiagnosticService.IMAGING && string.IsNullOrWhiteSpace(request.Modality))
            return BadRequest(new { error = "Imaging modality is required." });
        if (request.Service is DiagnosticService.PHYSIOLOGY or DiagnosticService.PATHOLOGY &&
            string.IsNullOrWhiteSpace(request.Modality))
            return BadRequest(new { error = "Diagnostic modality or case type is required." });
        var order = await _db.ClinicalOrders.FirstOrDefaultAsync(x => x.Id == request.ClinicalOrderId && x.PatientId == request.PatientId);
        if (order == null) return NotFound(new { error = "Clinical order not found." });
        if (request.Service == DiagnosticService.LABORATORY && string.IsNullOrWhiteSpace(request.SpecimenType))
            return BadRequest(new { error = "Laboratory specimen type is required." });
        var patient = await _db.Patients.Include(x => x.Person).FirstOrDefaultAsync(x => x.Id == request.PatientId);
        if (patient == null) return NotFound(new { error = "Patient not found." });
        var pregnancyRisk = request.Service == DiagnosticService.IMAGING &&
            request.Modality is "X_RAY" or "CT" or "MRI" &&
            string.Equals(patient.Person.SexAtBirth, "F", StringComparison.OrdinalIgnoreCase);
        if (pregnancyRisk && !request.PregnancySafetyAcknowledged)
            return Conflict(new { error = "Pregnancy safety assessment is required before this imaging order." });
        var item = new DiagnosticWorkItem
        {
            ClinicalOrderId = order.Id, PatientId = order.PatientId, Service = request.Service,
            SpecimenType = request.SpecimenType?.Trim(), Modality = request.Modality?.Trim(),
            PacsStudyUid = request.PacsStudyUid?.Trim(),
            PregnancySafetyAcknowledged = request.PregnancySafetyAcknowledged,
            AccessionNumber = $"{request.Service.ToString()[..3]}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..24],
            CreatedByUserAccountId = actor.UserAccountId
        };
        _db.DiagnosticWorkItems.Add(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DIAGNOSTIC_WORK_ITEM_CREATED", "DiagnosticWorkItem", item.Id.ToString(), actor.UserAccountId, item.PatientId,
            metadata: new { service = item.Service.ToString(), item.AccessionNumber });
        return Created($"/api/diagnostics/work-items/{item.Id}", new { id = item.Id, accessionNumber = item.AccessionNumber });
    }

    [HttpPost("work-items/{id:guid}/result")]
    public async Task<IActionResult> RecordResult(Guid id, [FromBody] ResultRequest request)
    {
        var actor = Actor();
        var item = await _db.DiagnosticWorkItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { error = "Diagnostic work item not found." });
        if (!CanAccess(actor, item.Service)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.ResultText)) return BadRequest(new { error = "Result text is required." });
        if (item.Status is DiagnosticWorkStatus.FINAL or DiagnosticWorkStatus.CANCELLED)
            return Conflict(new { error = "Closed diagnostic items cannot be updated." });
        item.ResultText = request.ResultText.Trim();
        item.IsCritical = request.IsCritical;
        item.CriticalCallbackNote = request.IsCritical ? request.CriticalCallbackNote?.Trim() : null;
        item.PerformedByUserAccountId = actor.UserAccountId;
        item.PerformedAt = DateTime.UtcNow;
        item.Status = request.Final ? DiagnosticWorkStatus.FINAL : DiagnosticWorkStatus.PRELIMINARY;
        item.FinalizedAt = request.Final ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(request.Final ? "DIAGNOSTIC_RESULT_FINALIZED" : "DIAGNOSTIC_RESULT_RECORDED",
            "DiagnosticWorkItem", id.ToString(), actor.UserAccountId, item.PatientId,
            metadata: new { item.Service, item.IsCritical });
        return Ok(new { id, status = item.Status.ToString(), isCritical = item.IsCritical });
    }

    private AccessTokenClaims Actor() => (AccessTokenClaims)HttpContext.Items["Actor"]!;
    private static bool CanAccess(AccessTokenClaims actor, DiagnosticService? service)
    {
        if (actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.DEPT_ADMIN) return true;
        if (service == DiagnosticService.LABORATORY) return actor.Role == UserRole.LAB;
        return (service is DiagnosticService.IMAGING or DiagnosticService.PHYSIOLOGY or DiagnosticService.PATHOLOGY)
            && (actor.Role is UserRole.CLINICIAN or UserRole.PHYSICIAN or UserRole.NURSE);
    }

    public sealed record CreateDiagnosticRequest(Guid ClinicalOrderId, Guid PatientId, DiagnosticService Service, string? SpecimenType = null, string? Modality = null, bool PregnancySafetyAcknowledged = false, string? PacsStudyUid = null);
    public sealed record ResultRequest(string ResultText, bool Final = false, bool IsCritical = false, string? CriticalCallbackNote = null);
}
