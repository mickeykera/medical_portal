using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Interoperability.Mpi;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/interop/mpi")]
[RequireAuth]
[RequireAdmin]
public class MpiController : ControllerBase
{
    private readonly IPatientDeduplicationService _deduplicationService;

    public MpiController(IPatientDeduplicationService deduplicationService)
    {
        _deduplicationService = deduplicationService;
    }

    [HttpPost("scan")]
    public async Task<IActionResult> ScanDuplicates(CancellationToken cancellationToken)
    {
        var count = await _deduplicationService.ScanForDuplicatesAsync(cancellationToken);
        return Ok(new { newlyDetectedCount = count });
    }

    [HttpGet("duplicates")]
    public async Task<IActionResult> GetPendingDuplicates(CancellationToken cancellationToken)
    {
        var candidates = await _deduplicationService.GetPendingCandidatesAsync(cancellationToken);
        return Ok(candidates);
    }

    [HttpPost("duplicates/{id}/resolve")]
    public async Task<IActionResult> ResolveDuplicate(Guid id, [FromBody] ResolveDuplicateRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Empty;
        var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(subClaim) && Guid.TryParse(subClaim, out var parsedId))
        {
            userId = parsedId;
        }

        var success = await _deduplicationService.ResolveDuplicateAsync(id, request.Resolution, userId, request.ResolutionNotes, cancellationToken);
        if (!success)
        {
            return NotFound(new { error = $"Duplicate candidate {id} not found or already resolved." });
        }

        return Ok(new { success = true, candidateId = id, resolution = request.Resolution.ToString() });
    }

    public record ResolveDuplicateRequest(DuplicateResolutionStatus Resolution, string? ResolutionNotes);
}
