using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly IPublicIpService _publicIpService;
    private readonly MedicalPortalDbContext _db;

    public SystemController(IPublicIpService publicIpService, MedicalPortalDbContext db)
    {
        _publicIpService = publicIpService;
        _db = db;
    }

    [HttpGet("ip")]
    [RequireAuth]
    public async Task<IActionResult> GetIpDetails()
    {
        var details = await _publicIpService.GetClientIpDetailsAsync(HttpContext);
        return Ok(details);
    }

    [HttpGet("db")]
    [RequireAuth]
    public async Task<IActionResult> GetDatabaseInfo()
    {
        var provider = _db.Database.ProviderName ?? "Unknown";
        var canConnect = await _db.Database.CanConnectAsync();

        return Ok(new
        {
            provider = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "PostgreSQL" :
                       provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "SQLite" : provider,
            canConnect,
            serverUtc = DateTime.UtcNow
        });
    }
}
