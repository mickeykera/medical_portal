using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Interoperability.Dhis2;

public class Dhis2AggregateService : IDhis2AggregateService
{
    private readonly MedicalPortalDbContext _db;
    private readonly IDhis2Client _dhis2Client;
    private readonly IOptions<Dhis2Options> _options;
    private readonly ILogger<Dhis2AggregateService> _logger;

    public Dhis2AggregateService(
        MedicalPortalDbContext db,
        IDhis2Client dhis2Client,
        IOptions<Dhis2Options> options,
        ILogger<Dhis2AggregateService>? logger = null)
    {
        _db = db;
        _dhis2Client = dhis2Client;
        _options = options;
        _logger = logger ?? NullLogger<Dhis2AggregateService>.Instance;
    }

    public async Task<Dhis2ReportDataset> GenerateDraftDatasetAsync(
        string period,
        string? dataSetId = null,
        string? orgUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _db.CurrentTenantId ?? TenantConstants.DefaultTenantId;
        var dsId = dataSetId ?? _options.Value.DefaultDataSetId;
        var ouId = orgUnitId ?? "ETH-AA-ADOT-01";

        // Aggregate core indicators from current database state
        var opdTotal = await _db.Appointments.CountAsync(cancellationToken);
        var patientsNew = await _db.Patients.CountAsync(cancellationToken);
#pragma warning disable CS0618
        var rxTotal = await _db.Prescriptions.CountAsync(cancellationToken);
        var labsTotal = await _db.Observations.CountAsync(o => o.Category == ObservationCategory.LAB, cancellationToken);
#pragma warning restore CS0618
        var formsTotal = await _db.FormResponses.CountAsync(cancellationToken);

        var dataValues = new List<Dhis2DataValueDto>
        {
            new("DE-OPD-TOTAL", period, ouId, opdTotal.ToString()),
            new("DE-PATIENT-NEW", period, ouId, patientsNew.ToString()),
            new("DE-RX-TOTAL", period, ouId, rxTotal.ToString()),
            new("DE-LAB-TOTAL", period, ouId, labsTotal.ToString()),
            new("DE-FORMS-SUBMITTED", period, ouId, formsTotal.ToString())
        };

        var report = new Dhis2ReportDataset
        {
            TenantId = tenantId,
            DataSetId = dsId,
            Period = period,
            OrgUnitId = ouId,
            Status = Dhis2ReportStatus.Draft,
            DataValuesJson = JsonSerializer.Serialize(dataValues),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Dhis2Reports.Add(report);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Generated draft DHIS2 report {ReportId} for period {Period}.", report.Id, period);
        return report;
    }

    public async Task<Dhis2ImportSummaryDto> ExecuteDryRunAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.Dhis2Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report == null)
        {
            throw new KeyNotFoundException($"DHIS2 report {reportId} not found.");
        }

        var values = JsonSerializer.Deserialize<List<Dhis2DataValueDto>>(report.DataValuesJson) ?? new List<Dhis2DataValueDto>();
        var payload = new Dhis2DataValueSetDto(report.DataSetId, report.Period, report.OrgUnitId, values);

        var result = await _dhis2Client.SubmitDataValueSetAsync(payload, dryRun: true, cancellationToken);

        report.DryRunSummaryJson = JsonSerializer.Serialize(result);
        if (report.Status == Dhis2ReportStatus.Draft)
        {
            report.Status = Dhis2ReportStatus.DryRun;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<bool> ApproveReportAsync(Guid reportId, string approvedBy, CancellationToken cancellationToken = default)
    {
        var report = await _db.Dhis2Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report == null) return false;

        report.ApprovedBy = approvedBy;
        report.ApprovedAtUtc = DateTime.UtcNow;
        report.Status = Dhis2ReportStatus.FacilityApproved;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Report {ReportId} approved by facility official {ApprovedBy}.", reportId, approvedBy);
        return true;
    }

    public async Task<Dhis2ImportSummaryDto> SubmitReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.Dhis2Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report == null)
        {
            throw new KeyNotFoundException($"DHIS2 report {reportId} not found.");
        }

        if (report.Status != Dhis2ReportStatus.FacilityApproved)
        {
            throw new InvalidOperationException($"Cannot submit DHIS2 report {reportId}: Facility approval is required prior to submission. Current status: {report.Status}");
        }

        var values = JsonSerializer.Deserialize<List<Dhis2DataValueDto>>(report.DataValuesJson) ?? new List<Dhis2DataValueDto>();
        var payload = new Dhis2DataValueSetDto(report.DataSetId, report.Period, report.OrgUnitId, values);

        var result = await _dhis2Client.SubmitDataValueSetAsync(payload, dryRun: false, cancellationToken);

        report.ImportSummaryJson = JsonSerializer.Serialize(result);
        report.SubmittedAtUtc = DateTime.UtcNow;
        report.Status = result.Status == "SUCCESS" ? Dhis2ReportStatus.Imported : Dhis2ReportStatus.Rejected;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Report {ReportId} submitted to DHIS2: {Status}.", reportId, result.Status);
        return result;
    }

    public async Task<Dhis2OfflineExportDto> GenerateSignedOfflineExportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.Dhis2Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report == null)
        {
            throw new KeyNotFoundException($"DHIS2 report {reportId} not found.");
        }

        var values = JsonSerializer.Deserialize<List<Dhis2DataValueDto>>(report.DataValuesJson) ?? new List<Dhis2DataValueDto>();
        var payload = new Dhis2DataValueSetDto(report.DataSetId, report.Period, report.OrgUnitId, values);
        var payloadJson = JsonSerializer.Serialize(payload);

        // Sign payload with HMAC-SHA256
        var secret = _options.Value.ExportHmacSecret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{report.DataSetId}:{report.Period}:{report.OrgUnitId}:{payloadJson}"));
        var signature = Convert.ToHexString(sigBytes).ToLowerInvariant();

        report.ExportPayloadJson = payloadJson;
        report.ExportSignature = signature;
        await _db.SaveChangesAsync(cancellationToken);

        return new Dhis2OfflineExportDto(
            ReportId: report.Id,
            DataSetId: report.DataSetId,
            Period: report.Period,
            OrgUnitId: report.OrgUnitId,
            PayloadJson: payloadJson,
            Signature: signature,
            ExportedAtUtc: DateTime.UtcNow
        );
    }

    public Task<Dhis2ReportDataset?> GetReportByIdAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        return _db.Dhis2Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
    }

    public async Task<IReadOnlyList<Dhis2ReportDataset>> GetReportsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Dhis2Reports
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
