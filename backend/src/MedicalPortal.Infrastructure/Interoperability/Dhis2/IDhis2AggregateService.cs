using MedicalPortal.Core.Entities;

namespace MedicalPortal.Infrastructure.Interoperability.Dhis2;

public record Dhis2ReportSummaryDto(
    Guid Id,
    string DataSetId,
    string Period,
    string OrgUnitId,
    string Status,
    int ValueCount,
    string? ApprovedBy,
    DateTime? ApprovedAtUtc,
    DateTime? SubmittedAtUtc,
    string? ImportSummary
);

public record Dhis2OfflineExportDto(
    Guid ReportId,
    string DataSetId,
    string Period,
    string OrgUnitId,
    string PayloadJson,
    string Signature,
    DateTime ExportedAtUtc
);

public interface IDhis2AggregateService
{
    Task<Dhis2ReportDataset> GenerateDraftDatasetAsync(
        string period,
        string? dataSetId = null,
        string? orgUnitId = null,
        CancellationToken cancellationToken = default
    );

    Task<Dhis2ImportSummaryDto> ExecuteDryRunAsync(
        Guid reportId,
        CancellationToken cancellationToken = default
    );

    Task<bool> ApproveReportAsync(
        Guid reportId,
        string approvedBy,
        CancellationToken cancellationToken = default
    );

    Task<Dhis2ImportSummaryDto> SubmitReportAsync(
        Guid reportId,
        CancellationToken cancellationToken = default
    );

    Task<Dhis2OfflineExportDto> GenerateSignedOfflineExportAsync(
        Guid reportId,
        CancellationToken cancellationToken = default
    );

    Task<Dhis2ReportDataset?> GetReportByIdAsync(
        Guid reportId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<Dhis2ReportDataset>> GetReportsAsync(
        CancellationToken cancellationToken = default
    );
}
