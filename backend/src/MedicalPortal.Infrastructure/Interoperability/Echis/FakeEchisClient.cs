namespace MedicalPortal.Infrastructure.Interoperability.Echis;

public class FakeEchisClient : IEchisClient
{
    private readonly object _lock = new();

    public bool IsOnline { get; set; } = true;
    public List<EchisReferralDto> OutboundReferrals { get; } = new();
    public List<EchisReferralDto> InboundReferralQueue { get; } = new();
    public Dictionary<string, (string Status, string? Notes)> StatusUpdates { get; } = new();

    public FakeEchisClient()
    {
        SeedDefaultInboundReferrals();
    }

    public Task<EchisReferralResponseDto> SendReferralAsync(EchisReferralDto referral, CancellationToken cancellationToken = default)
    {
        if (!IsOnline)
        {
            throw new HttpRequestException("eCHIS national gateway unreachable.");
        }

        lock (_lock)
        {
            OutboundReferrals.Add(referral);
            return Task.FromResult(new EchisReferralResponseDto(true, referral.ReferralNumber, "Referral transmitted to national eCHIS gateway."));
        }
    }

    public Task<IReadOnlyList<EchisReferralDto>> FetchInboundReferralsAsync(string facilityCode, CancellationToken cancellationToken = default)
    {
        if (!IsOnline)
        {
            throw new HttpRequestException("eCHIS national gateway unreachable.");
        }

        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<EchisReferralDto>>(InboundReferralQueue.ToList());
        }
    }

    public Task<bool> UpdateReferralStatusAsync(string referralNumber, string status, string? notes = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            StatusUpdates[referralNumber] = (status, notes);
            return Task.FromResult(true);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            IsOnline = true;
            OutboundReferrals.Clear();
            InboundReferralQueue.Clear();
            StatusUpdates.Clear();
            SeedDefaultInboundReferrals();
        }
    }

    private void SeedDefaultInboundReferrals()
    {
        InboundReferralQueue.Add(new EchisReferralDto(
            ReferralNumber: "ECHIS-REF-2026-0001",
            Direction: "Inbound",
            NationalPatientId: "FAYYA-ETH-9912",
            PatientFullName: "Almaz Kebede",
            PatientPhone: "+251911223344",
            ReferringUnit: "Woreda 03 Kebele 05 Health Post",
            ReceivingUnit: "Adot Internal Medicine",
            ReasonForReferral: "Uncontrolled hypertension and severe gestational edema",
            Urgency: "Urgent",
            Status: "PendingTriage",
            ClinicalSummaryJson: "{\"bpSystolic\":170,\"bpDiastolic\":110,\"dangerSigns\":[\"Severe headache\",\"Edema\"]}",
            CreatedAtUtc: DateTime.UtcNow.AddHours(-2)
        ));
    }
}
