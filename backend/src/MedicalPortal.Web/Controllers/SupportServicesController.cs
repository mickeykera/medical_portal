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
public class SupportServicesController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public SupportServicesController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> Tasks()
    {
        if (!CanManage(Actor())) return Forbid();
        return Ok(new { tasks = await _db.SupportServiceTasks.AsNoTracking().Where(x => x.Status != SupportTaskStatus.COMPLETED && x.Status != SupportTaskStatus.CANCELLED).OrderBy(x => x.CreatedAt).ToListAsync() });
    }

    [HttpPost("tasks/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var actor = Actor();
        if (!CanManage(actor)) return Forbid();
        var task = await _db.SupportServiceTasks.FirstOrDefaultAsync(x => x.Id == id);
        if (task == null) return NotFound(new { error = "Support task not found." });
        if (task.Status == SupportTaskStatus.COMPLETED) return Conflict(new { error = "Support task is already complete." });
        task.Status = SupportTaskStatus.COMPLETED;
        task.CompletedAt = DateTime.UtcNow;
        if (task.Type == SupportTaskType.TERMINAL_CLEANING && task.BedId.HasValue)
        {
            var bed = await _db.Beds.FirstOrDefaultAsync(x => x.Id == task.BedId.Value);
            if (bed != null && bed.State == BedState.CLEANING && !bed.CurrentEncounterId.HasValue)
                bed.State = BedState.AVAILABLE;
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SUPPORT_TASK_COMPLETED", "SupportServiceTask", id.ToString(), actor.UserAccountId,
            metadata: new { task.Type });
        return Ok(new { id, status = task.Status.ToString() });
    }

    private AccessTokenClaims Actor() => (AccessTokenClaims)HttpContext.Items["Actor"]!;
    private static bool CanManage(AccessTokenClaims actor) =>
        actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.DEPT_ADMIN or UserRole.NURSE;
}
