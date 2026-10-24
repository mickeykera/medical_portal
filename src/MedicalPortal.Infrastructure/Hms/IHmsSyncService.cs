using MedicalPortal.Core.Entities;

namespace MedicalPortal.Infrastructure.Hms;

public interface IHmsSyncService
{
    Task<bool> SyncPatientAsync(Guid patientId, CancellationToken cancellationToken = default);

    Task<int> SyncStalePatientsAsync(TimeSpan? stalenessThreshold = null, CancellationToken cancellationToken = default);

    Task<int> SyncOutboundAppointmentsAsync(CancellationToken cancellationToken = default);

    Task<int> SyncOutboundRefillsAsync(CancellationToken cancellationToken = default);
}
