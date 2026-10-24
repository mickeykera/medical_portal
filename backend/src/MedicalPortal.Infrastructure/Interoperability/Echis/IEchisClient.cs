namespace MedicalPortal.Infrastructure.Interoperability.Echis;

public record EchisReferralDto(
    string ReferralNumber,
    string Direction,
    string? NationalPatientId,
    string PatientFullName,
    string PatientPhone,
    string ReferringUnit,
    string ReceivingUnit,
    string ReasonForReferral,
    string Urgency,
    string Status,
    string ClinicalSummaryJson,
    DateTime CreatedAtUtc
);

public record EchisReferralResponseDto(
    bool Success,
    string ReferralNumber,
    string? Message
);

public interface IEchisClient
{
    Task<EchisReferralResponseDto> SendReferralAsync(
        EchisReferralDto referral,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<EchisReferralDto>> FetchInboundReferralsAsync(
        string facilityCode,
        CancellationToken cancellationToken = default
    );

    Task<bool> UpdateReferralStatusAsync(
        string referralNumber,
        string status,
        string? notes = null,
        CancellationToken cancellationToken = default
    );
}
