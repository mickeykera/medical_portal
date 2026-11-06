using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/kpis")]
[RequireAuth]
public class KpiController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;

    public KpiController(MedicalPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        if (!CanRead(Actor())) return Forbid();
        var start = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var end = to ?? DateTime.UtcNow;
        if (start >= end) return BadRequest(new { error = "KPI period must have a positive duration." });
        var definitions = await _db.KpiDefinitions.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync();
        var snapshots = await _db.KpiSnapshots.AsNoTracking()
            .Where(x => x.PeriodStart >= start && x.PeriodEnd <= end)
            .GroupBy(x => x.KpiDefinitionId)
            .Select(g => g.OrderByDescending(x => x.CalculatedAt).First())
            .ToDictionaryAsync(x => x.KpiDefinitionId);
        return Ok(new
        {
            periodStart = start, periodEnd = end,
            indicators = definitions.Select(x => new
            {
                code = x.Code, name = x.Name, category = x.Category,
                numeratorMetric = x.NumeratorMetric, denominatorMetric = x.DenominatorMetric,
                value = snapshots.TryGetValue(x.Id, out var snapshot) ? snapshot.Value : 0,
                unit = snapshots.TryGetValue(x.Id, out snapshot) ? snapshot.Unit : "count"
            })
        });
    }

    [HttpPost("definitions")]
    public async Task<IActionResult> CreateDefinition([FromBody] CreateDefinitionRequest request)
    {
        if (Actor().Role is not (UserRole.ADMIN or UserRole.SYSTEM_ADMIN)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Category) || string.IsNullOrWhiteSpace(request.NumeratorMetric))
            return BadRequest(new { error = "Code, name, category, and numerator metric are required." });
        if (await _db.KpiDefinitions.AnyAsync(x => x.Code == request.Code.Trim()))
            return Conflict(new { error = "KPI code already exists." });
        var definition = new KpiDefinition
        {
            Code = request.Code.Trim(), Name = request.Name.Trim(), Category = request.Category.Trim(),
            NumeratorMetric = request.NumeratorMetric.Trim(), DenominatorMetric = request.DenominatorMetric?.Trim()
        };
        _db.KpiDefinitions.Add(definition);
        await _db.SaveChangesAsync();
        return Created($"/api/kpis/definitions/{definition.Id}", new { id = definition.Id, code = definition.Code });
    }

    private AccessTokenClaims Actor() => (AccessTokenClaims)HttpContext.Items["Actor"]!;
    private static bool CanRead(AccessTokenClaims actor) =>
        actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.DEPT_ADMIN or UserRole.COMPLIANCE_AUDITOR;

    public sealed record CreateDefinitionRequest(string Code, string Name, string Category, string NumeratorMetric, string? DenominatorMetric = null);
}
