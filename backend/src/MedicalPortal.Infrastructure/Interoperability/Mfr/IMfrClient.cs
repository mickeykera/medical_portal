namespace MedicalPortal.Infrastructure.Interoperability.Mfr;

public record MfrFacilityDto(
    string MfrId,
    string Name,
    string Tier,
    string Region,
    string Zone,
    string Woreda,
    string Kebele,
    string OperationalStatus
);

public interface IMfrClient
{
    Task<IReadOnlyList<MfrFacilityDto>> FetchFacilitiesAsync(CancellationToken cancellationToken = default);
    Task<MfrFacilityDto?> GetFacilityByMfrIdAsync(string mfrId, CancellationToken cancellationToken = default);
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
