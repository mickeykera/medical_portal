using MedicalPortal.Infrastructure.Offline;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/system")]
public class SystemNodeController : ControllerBase
{
    private readonly IOutboxSyncDispatcher _outboxDispatcher;
    private readonly IReferenceDataService _referenceService;
    private readonly IOptions<FacilityNodeOptions> _options;

    public SystemNodeController(
        IOutboxSyncDispatcher outboxDispatcher,
        IReferenceDataService referenceService,
        IOptions<FacilityNodeOptions> options)
    {
        _outboxDispatcher = outboxDispatcher;
        _referenceService = referenceService;
        _options = options;
    }

    /// <summary>
    /// Returns operational node status, queue depth, connectivity, and active reference packages.
    /// </summary>
    [HttpGet("node-status")]
    public async Task<IActionResult> GetNodeStatus(CancellationToken cancellationToken)
    {
        var outboxStatus = await _outboxDispatcher.GetStatusAsync(cancellationToken);
        var activePackages = await _referenceService.GetActivePackagesAsync(cancellationToken);

        return Ok(new
        {
            nodeRole = _options.Value.NodeRole.ToString(),
            facilityCode = _options.Value.FacilityCode,
            facilityName = _options.Value.FacilityName,
            isOnline = outboxStatus.AggregatorReachable,
            outbox = new
            {
                pendingCount = outboxStatus.PendingCount,
                deliveredCount = outboxStatus.DeliveredCount,
                failedTransientCount = outboxStatus.FailedTransientCount,
                deadLetterCount = outboxStatus.DeadLetterCount,
                totalDepth = outboxStatus.TotalDepth,
                lastDeliveredAtUtc = outboxStatus.LastDeliveredAtUtc
            },
            activeReferencePackages = activePackages.Select(p => new
            {
                packageId = p.PackageId,
                name = p.Name,
                version = p.Version,
                versionOrdinal = p.VersionOrdinal,
                signedBy = p.SignedBy,
                installedAtUtc = p.InstalledAtUtc
            })
        });
    }

    /// <summary>
    /// Manually triggers a store-and-forward outbox synchronization cycle.
    /// </summary>
    [HttpPost("outbox/drain-sync")]
    [RequireAuth]
    [RequireAdmin]
    public async Task<IActionResult> DrainOutboxSync(CancellationToken cancellationToken)
    {
        var report = await _outboxDispatcher.DispatchPendingMessagesAsync(cancellationToken);
        return Ok(new
        {
            processed = report.Processed,
            delivered = report.Delivered,
            failed = report.Failed,
            deadLettered = report.DeadLettered,
            remainingQueueDepth = report.RemainingQueueDepth,
            linkAvailable = report.LinkAvailable
        });
    }

    /// <summary>
    /// Installs a signed reference package bundle via offline import (e.g. from USB drive or regional media).
    /// </summary>
    [HttpPost("reference-packages/install")]
    [RequireAuth]
    [RequireAdmin]
    public async Task<IActionResult> InstallReferencePackage([FromBody] ReferencePackageDto packageDto, CancellationToken cancellationToken)
    {
        var result = await _referenceService.ValidateAndInstallPackageAsync(packageDto, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage, packageId = result.PackageId, version = result.Version });
        }

        return Ok(new { success = true, packageId = result.PackageId, version = result.Version });
    }

    /// <summary>
    /// Lists all installed reference packages.
    /// </summary>
    [HttpGet("reference-packages")]
    [RequireAuth]
    public async Task<IActionResult> GetReferencePackages(CancellationToken cancellationToken)
    {
        var packages = await _referenceService.GetActivePackagesAsync(cancellationToken);
        return Ok(packages.Select(p => new
        {
            id = p.Id,
            packageId = p.PackageId,
            name = p.Name,
            version = p.Version,
            versionOrdinal = p.VersionOrdinal,
            signedBy = p.SignedBy,
            signedAtUtc = p.SignedAtUtc,
            installedAtUtc = p.InstalledAtUtc,
            status = p.Status.ToString()
        }));
    }
}
