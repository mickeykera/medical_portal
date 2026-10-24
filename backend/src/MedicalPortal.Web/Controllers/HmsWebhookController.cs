using System.Text;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Hms;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/integrations/hms")]
public class HmsWebhookController : ControllerBase
{
    private readonly IHmsWebhookHandler _webhookHandler;
    private readonly IHmsConflictService _conflictService;
    private readonly IHmsDeadLetterService _deadLetterService;

    public HmsWebhookController(
        IHmsWebhookHandler webhookHandler,
        IHmsConflictService conflictService,
        IHmsDeadLetterService deadLetterService)
    {
        _webhookHandler = webhookHandler;
        _conflictService = conflictService;
        _deadLetterService = deadLetterService;
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleWebhook()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync();

        var signatureHeader = Request.Headers["X-Hms-Signature"].FirstOrDefault()
            ?? Request.Headers["HMS-Signature"].FirstOrDefault()
            ?? string.Empty;

        var result = await _webhookHandler.ProcessWebhookAsync(signatureHeader, rawBody, HttpContext.RequestAborted);
        if (!result.IsValid)
        {
            return Unauthorized(new { error = result.ErrorMessage ?? "Signature verification failed" });
        }

        return Ok(new
        {
            ok = true,
            eventId = result.Payload?.EventId,
            duplicate = result.IsDuplicate
        });
    }

    [HttpGet("conflicts")]
    [RequireAuth]
    [RequireAdmin]
    public async Task<IActionResult> GetConflicts([FromQuery] Guid? patientId)
    {
        var conflicts = await _conflictService.GetPendingConflictsAsync(patientId, HttpContext.RequestAborted);
        return Ok(conflicts);
    }

    public record ResolveConflictBody(bool AcceptHms, string? Notes);

    [HttpPost("conflicts/{id:guid}/resolve")]
    [RequireAuth]
    [RequireAdmin]
    public async Task<IActionResult> ResolveConflict(Guid id, [FromBody] ResolveConflictBody body)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (actor.Role is not (UserRole.ADMIN or UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Resolving demographic conflicts is restricted to system administrators." });
        }

        var resolved = await _conflictService.ResolveConflictAsync(
            id,
            body.AcceptHms,
            actor.UserAccountId,
            body.Notes,
            HttpContext.RequestAborted);

        if (resolved == null)
            return NotFound(new { error = "Demographic conflict record not found" });

        return Ok(new { ok = true, conflict = resolved });
    }

    [HttpGet("dead-letters")]
    [RequireAuth]
    [RequireAdmin]
    public async Task<IActionResult> GetDeadLetters([FromQuery] HmsDeadLetterStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        var deadLetters = await _deadLetterService.GetDeadLettersAsync(status, page, pageSize, actor.UserAccountId, HttpContext.RequestAborted);
        return Ok(deadLetters);
    }

    public record ResolveDeadLetterBody(HmsDeadLetterStatus Status, string? Notes);

    [HttpPost("dead-letters/{id:guid}/resolve")]
    [RequireAuth]
    [RequireAdmin]
    public async Task<IActionResult> ResolveDeadLetter(Guid id, [FromBody] ResolveDeadLetterBody body)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (actor.Role is not (UserRole.ADMIN or UserRole.SYSTEM_ADMIN))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Resolving dead letters is restricted to system administrators." });
        }

        var success = await _deadLetterService.ResolveDeadLetterAsync(id, body.Status, actor.UserAccountId, body.Notes, HttpContext.RequestAborted);
        if (!success)
            return NotFound(new { error = "Dead-letter record not found" });

        return Ok(new { ok = true, status = body.Status.ToString() });
    }
}
