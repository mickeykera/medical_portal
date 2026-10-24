using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Infrastructure.Hms;

public interface IHmsConflictService
{
    Task<List<HmsDemographicConflict>> DetectAndReconcileDemographicsAsync(
        Patient patient,
        HmsPatientDto hmsPatient,
        CancellationToken cancellationToken = default);

    Task<HmsDemographicConflict?> ResolveConflictAsync(
        Guid conflictId,
        bool acceptHms,
        Guid actorUserAccountId,
        string? notes = null,
        CancellationToken cancellationToken = default);

    Task<List<HmsDemographicConflict>> GetPendingConflictsAsync(
        Guid? patientId = null,
        CancellationToken cancellationToken = default);
}
