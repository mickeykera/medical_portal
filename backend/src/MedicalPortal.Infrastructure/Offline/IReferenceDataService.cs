using MedicalPortal.Core.Entities;

namespace MedicalPortal.Infrastructure.Offline;

public record ReferencePackageInstallResultDto(
    bool Success,
    string PackageId,
    string Version,
    string? ErrorMessage
);

public interface IReferenceDataService
{
    Task<ReferencePackageInstallResultDto> ValidateAndInstallPackageAsync(
        ReferencePackageDto dto,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<ReferencePackageInstallResultDto>> PullAndApplyOnlinePackagesAsync(
        CancellationToken cancellationToken = default
    );

    Task<ReferencePackageInstallResultDto> ImportPackageBundleJsonAsync(
        string bundleJson,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<ReferencePackage>> GetActivePackagesAsync(
        CancellationToken cancellationToken = default
    );
}
