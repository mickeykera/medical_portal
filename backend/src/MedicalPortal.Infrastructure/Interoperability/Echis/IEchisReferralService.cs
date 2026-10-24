using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Infrastructure.Interoperability.Echis;

public record CreateOutboundReferralInputDto(
    Guid? PatientId,
    string? NationalPatientId,
    string PatientFullName,
    string PatientPhone,
    string ReceivingUnit,
    string ReasonForReferral,
    string Urgency,
    string ClinicalSummaryJson
);

public interface IEchisReferralService
{
    bool IsFeatureEnabled();

    Task<EchisReferral> CreateOutboundReferralAsync(
        CreateOutboundReferralInputDto input,
        CancellationToken cancellationToken = default
    );

    Task<int> SyncInboundReferralsAsync(CancellationToken cancellationToken = default);

    Task<bool> TriageReferralAsync(
        Guid referralId,
        EchisReferralStatus newStatus,
        string? triageNotes = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<EchisReferral>> GetReferralsAsync(
        EchisReferralDirection? direction = null,
        EchisReferralStatus? status = null,
        CancellationToken cancellationToken = default
    );
}
