namespace MedicalPortal.Infrastructure.Interoperability.Dhis2;

public class FakeDhis2Client : IDhis2Client
{
    private readonly object _lock = new();

    public bool IsOnline { get; set; } = true;
    public bool SimulateValidationConflict { get; set; } = false;

    public List<Dhis2DataValueSetDto> Submissions { get; } = new();
    public List<Dhis2DataValueSetDto> DryRuns { get; } = new();

    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(IsOnline);
    }

    public Task<Dhis2ImportSummaryDto> SubmitDataValueSetAsync(
        Dhis2DataValueSetDto payload,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsOnline)
        {
            throw new HttpRequestException("DHIS2 server unreachable (offline).");
        }

        if (SimulateValidationConflict)
        {
            return Task.FromResult(new Dhis2ImportSummaryDto(
                Status: "WARNING",
                Description: "Validation failed for one or more data elements",
                ImportCount: new Dhis2ImportCountDto(0, 0, payload.DataValues.Count, 0),
                Conflicts: new List<string> { "Data element DE-OPD-TOTAL is out of allowable range for orgUnit." }
            ));
        }

        lock (_lock)
        {
            if (dryRun)
            {
                DryRuns.Add(payload);
                return Task.FromResult(new Dhis2ImportSummaryDto(
                    Status: "SUCCESS",
                    Description: "Dry run validation completed successfully: 0 conflicts detected",
                    ImportCount: new Dhis2ImportCountDto(payload.DataValues.Count, 0, 0, 0),
                    Conflicts: null
                ));
            }

            var existingIndex = Submissions.FindIndex(s =>
                s.DataSet == payload.DataSet &&
                s.Period == payload.Period &&
                s.OrgUnit == payload.OrgUnit);

            if (existingIndex >= 0)
            {
                // Idempotent re-submission update
                Submissions[existingIndex] = payload;
                return Task.FromResult(new Dhis2ImportSummaryDto(
                    Status: "SUCCESS",
                    Description: "Import process completed successfully (idempotent update)",
                    ImportCount: new Dhis2ImportCountDto(0, payload.DataValues.Count, 0, 0),
                    Conflicts: null
                ));
            }

            Submissions.Add(payload);
            return Task.FromResult(new Dhis2ImportSummaryDto(
                Status: "SUCCESS",
                Description: "Import process completed successfully",
                ImportCount: new Dhis2ImportCountDto(payload.DataValues.Count, 0, 0, 0),
                Conflicts: null
            ));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            IsOnline = true;
            SimulateValidationConflict = false;
            Submissions.Clear();
            DryRuns.Clear();
        }
    }
}
