namespace MedicalPortal.Infrastructure.Interoperability.Dhis2;

public record Dhis2DataValueDto(
    string DataElement,
    string Period,
    string OrgUnit,
    string Value,
    string? CategoryOptionCombo = null,
    string? Comment = null
);

public record Dhis2DataValueSetDto(
    string DataSet,
    string Period,
    string OrgUnit,
    IReadOnlyList<Dhis2DataValueDto> DataValues
);

public record Dhis2ImportCountDto(
    int Imported,
    int Updated,
    int Ignored,
    int Deleted
);

public record Dhis2ImportSummaryDto(
    string Status, // "SUCCESS", "WARNING", "ERROR"
    string Description,
    Dhis2ImportCountDto ImportCount,
    IReadOnlyList<string>? Conflicts = null
);

public interface IDhis2Client
{
    Task<Dhis2ImportSummaryDto> SubmitDataValueSetAsync(
        Dhis2DataValueSetDto payload,
        bool dryRun = false,
        CancellationToken cancellationToken = default
    );

    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
