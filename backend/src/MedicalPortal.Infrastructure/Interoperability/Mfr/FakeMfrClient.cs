namespace MedicalPortal.Infrastructure.Interoperability.Mfr;

public class FakeMfrClient : IMfrClient
{
    private readonly List<MfrFacilityDto> _facilities = new()
    {
        new("ETH-AA-001", "Tikur Anbessa Specialized Hospital", "Specialized Hospital", "Addis Ababa", "Lideta", "Woreda 08", "01", "Operational"),
        new("ETH-AA-002", "St. Paul's Hospital Millennium Medical College", "Specialized Hospital", "Addis Ababa", "Gulele", "Woreda 09", "02", "Operational"),
        new("ETH-AA-003", "Yekatit 12 Hospital Medical College", "General Hospital", "Addis Ababa", "Arada", "Woreda 01", "03", "Operational"),
        new("ETH-AA-004", "Adot Internal Medicine", "General Hospital", "Addis Ababa", "Kirkos", "Woreda 03", "04", "Operational")
    };

    public bool IsOnline { get; set; } = true;

    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(IsOnline);
    }

    public Task<IReadOnlyList<MfrFacilityDto>> FetchFacilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOnline)
        {
            throw new HttpRequestException("MFR national directory unreachable.");
        }

        return Task.FromResult<IReadOnlyList<MfrFacilityDto>>(_facilities.ToList());
    }

    public Task<MfrFacilityDto?> GetFacilityByMfrIdAsync(string mfrId, CancellationToken cancellationToken = default)
    {
        var fac = _facilities.FirstOrDefault(f => f.MfrId.Equals(mfrId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(fac);
    }
}
