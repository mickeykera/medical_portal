using MedicalPortal.Infrastructure.Interoperability.Dhis2;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/interop/dhis2")]
[RequireAuth]
[RequireAdmin]
public class Dhis2Controller : ControllerBase
{
    private readonly IDhis2AggregateService _aggregateService;

    public Dhis2Controller(IDhis2AggregateService aggregateService)
    {
        _aggregateService = aggregateService;
    }

    [HttpPost("draft")]
    public async Task<IActionResult> GenerateDraft([FromBody] GenerateDraftRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Period))
        {
            return BadRequest(new { error = "Reporting period is required (e.g. '201902' or '2019Q1')." });
        }

        var report = await _aggregateService.GenerateDraftDatasetAsync(request.Period, request.DataSetId, request.OrgUnitId, cancellationToken);
        return Ok(new
        {
            reportId = report.Id,
            dataSetId = report.DataSetId,
            period = report.Period,
            orgUnitId = report.OrgUnitId,
            status = report.Status.ToString(),
            createdAtUtc = report.CreatedAtUtc
        });
    }

    [HttpPost("{id}/dry-run")]
    public async Task<IActionResult> DryRun(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _aggregateService.ExecuteDryRunAsync(id, cancellationToken);
            return Ok(summary);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"DHIS2 report {id} not found." });
        }
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var approver = User.Identity?.Name ?? "Facility Leadership";
        var success = await _aggregateService.ApproveReportAsync(id, approver, cancellationToken);
        if (!success)
        {
            return NotFound(new { error = $"DHIS2 report {id} not found." });
        }

        return Ok(new { success = true, approvedBy = approver, status = "FacilityApproved" });
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _aggregateService.SubmitReportAsync(id, cancellationToken);
            return Ok(summary);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"DHIS2 report {id} not found." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var export = await _aggregateService.GenerateSignedOfflineExportAsync(id, cancellationToken);
            return Ok(export);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"DHIS2 report {id} not found." });
        }
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports(CancellationToken cancellationToken)
    {
        var reports = await _aggregateService.GetReportsAsync(cancellationToken);
        return Ok(reports.Select(r => new
        {
            id = r.Id,
            dataSetId = r.DataSetId,
            period = r.Period,
            orgUnitId = r.OrgUnitId,
            status = r.Status.ToString(),
            approvedBy = r.ApprovedBy,
            approvedAtUtc = r.ApprovedAtUtc,
            submittedAtUtc = r.SubmittedAtUtc,
            createdAtUtc = r.CreatedAtUtc
        }));
    }

    public record GenerateDraftRequest(string Period, string? DataSetId, string? OrgUnitId);
}
