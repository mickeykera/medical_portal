using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Interoperability.Mfr;

public class MfrSyncService : IMfrSyncService
{
    private readonly MedicalPortalDbContext _db;
    private readonly IMfrClient _mfrClient;
    private readonly ILogger<MfrSyncService> _logger;

    public MfrSyncService(
        MedicalPortalDbContext db,
        IMfrClient mfrClient,
        ILogger<MfrSyncService>? logger = null)
    {
        _db = db;
        _mfrClient = mfrClient;
        _logger = logger ?? NullLogger<MfrSyncService>.Instance;
    }

    public async Task<MfrSyncResultDto> SyncFacilitiesWithMfrAsync(CancellationToken cancellationToken = default)
    {
        var mfrFacilities = await _mfrClient.FetchFacilitiesAsync(cancellationToken);
        var localFacilities = await _db.Facilities.ToListAsync(cancellationToken);

        int reconciled = 0;
        int unmatched = 0;

        foreach (var loc in localFacilities)
        {
            // Match by existing MfrCode or name
            var match = mfrFacilities.FirstOrDefault(m =>
                (!string.IsNullOrWhiteSpace(loc.MfrCode) && m.MfrId.Equals(loc.MfrCode, StringComparison.OrdinalIgnoreCase)) ||
                m.Name.Equals(loc.Name, StringComparison.OrdinalIgnoreCase) ||
                loc.Name.Contains(m.Name, StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains(loc.Name, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                loc.MfrCode = match.MfrId;
                loc.FacilityType = match.Tier;
                loc.Region = match.Region;
                loc.Zone = match.Zone;
                loc.Woreda = match.Woreda;
                loc.OperationalStatus = match.OperationalStatus;
                loc.MfrLastSyncedAtUtc = DateTime.UtcNow;
                reconciled++;
            }
            else
            {
                unmatched++;
            }
        }

        if (reconciled > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Reconciled {Count} local facilities with Master Facility Registry.", reconciled);
        }

        return new MfrSyncResultDto(mfrFacilities.Count, reconciled, unmatched);
    }
}
