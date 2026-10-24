using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Interoperability.Mfr;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/interop/mfr")]
[RequireAuth]
public class MfrController : ControllerBase
{
    private readonly IMfrSyncService _mfrSyncService;
    private readonly MedicalPortalDbContext _db;

    public MfrController(IMfrSyncService mfrSyncService, MedicalPortalDbContext db)
    {
        _mfrSyncService = mfrSyncService;
        _db = db;
    }

    [HttpPost("sync")]
    [RequireAdmin]
    public async Task<IActionResult> SyncFacilities(CancellationToken cancellationToken)
    {
        var result = await _mfrSyncService.SyncFacilitiesWithMfrAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("facilities")]
    public async Task<IActionResult> GetFacilities(CancellationToken cancellationToken)
    {
        var facilities = await _db.Facilities
            .Select(f => new
            {
                id = f.Id,
                name = f.Name,
                code = f.Code,
                mfrCode = f.MfrCode,
                facilityType = f.FacilityType,
                region = f.Region,
                zone = f.Zone,
                woreda = f.Woreda,
                operationalStatus = f.OperationalStatus,
                mfrLastSyncedAtUtc = f.MfrLastSyncedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(facilities);
    }
}
