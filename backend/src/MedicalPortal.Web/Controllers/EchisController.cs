using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Interoperability.Echis;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/interop/echis")]
[RequireAuth]
[RequireModule("echis")]
public class EchisController : ControllerBase
{
    private readonly IEchisReferralService _referralService;

    public EchisController(IEchisReferralService referralService)
    {
        _referralService = referralService;
    }

    [HttpPost("referrals/outbound")]
    [RequireClinician]
    public async Task<IActionResult> CreateOutboundReferral([FromBody] CreateOutboundReferralInputDto input, CancellationToken cancellationToken)
    {
        if (!_referralService.IsFeatureEnabled())
        {
            return StatusCode(503, new { error = "eCHIS community referral integration is disabled by feature flag." });
        }

        var referral = await _referralService.CreateOutboundReferralAsync(input, cancellationToken);
        return Ok(new
        {
            referralId = referral.Id,
            referralNumber = referral.ReferralNumber,
            status = referral.Status.ToString(),
            direction = referral.Direction.ToString(),
            createdAtUtc = referral.CreatedAtUtc
        });
    }

    [HttpPost("sync-inbound")]
    [RequireClinician]
    public async Task<IActionResult> SyncInboundReferrals(CancellationToken cancellationToken)
    {
        if (!_referralService.IsFeatureEnabled())
        {
            return StatusCode(503, new { error = "eCHIS community referral integration is disabled by feature flag." });
        }

        var count = await _referralService.SyncInboundReferralsAsync(cancellationToken);
        return Ok(new { syncedCount = count });
    }

    [HttpPost("referrals/{id}/triage")]
    [RequireClinician]
    public async Task<IActionResult> TriageReferral(Guid id, [FromBody] TriageRequest request, CancellationToken cancellationToken)
    {
        if (!_referralService.IsFeatureEnabled())
        {
            return StatusCode(503, new { error = "eCHIS community referral integration is disabled by feature flag." });
        }

        var success = await _referralService.TriageReferralAsync(id, request.Status, request.TriageNotes, cancellationToken);
        if (!success)
        {
            return NotFound(new { error = $"eCHIS referral {id} not found." });
        }

        return Ok(new { success = true, referralId = id, status = request.Status.ToString() });
    }

    [HttpGet("referrals")]
    public async Task<IActionResult> GetReferrals(
        [FromQuery] EchisReferralDirection? direction,
        [FromQuery] EchisReferralStatus? status,
        CancellationToken cancellationToken)
    {
        var referrals = await _referralService.GetReferralsAsync(direction, status, cancellationToken);
        return Ok(referrals.Select(r => new
        {
            id = r.Id,
            referralNumber = r.ReferralNumber,
            direction = r.Direction.ToString(),
            patientId = r.PatientId,
            nationalPatientId = r.NationalPatientId,
            patientFullName = r.PatientFullName,
            patientPhone = r.PatientPhone,
            referringUnit = r.ReferringUnit,
            receivingUnit = r.ReceivingUnit,
            reasonForReferral = r.ReasonForReferral,
            urgency = r.Urgency,
            status = r.Status.ToString(),
            createdAtUtc = r.CreatedAtUtc,
            triagedAtUtc = r.TriagedAtUtc,
            triageNotes = r.TriageNotes
        }));
    }

    public record TriageRequest(EchisReferralStatus Status, string? TriageNotes);
}
