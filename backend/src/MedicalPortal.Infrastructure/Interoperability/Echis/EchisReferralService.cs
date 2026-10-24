using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Interoperability.Echis;

public class EchisReferralService : IEchisReferralService
{
    private readonly MedicalPortalDbContext _db;
    private readonly IEchisClient _echisClient;
    private readonly IOptions<EchisOptions> _options;
    private readonly ILogger<EchisReferralService> _logger;

    public EchisReferralService(
        MedicalPortalDbContext db,
        IEchisClient echisClient,
        IOptions<EchisOptions> options,
        ILogger<EchisReferralService>? logger = null)
    {
        _db = db;
        _echisClient = echisClient;
        _options = options;
        _logger = logger ?? NullLogger<EchisReferralService>.Instance;
    }

    public bool IsFeatureEnabled() => _options.Value.Enabled;

    public async Task<EchisReferral> CreateOutboundReferralAsync(
        CreateOutboundReferralInputDto input,
        CancellationToken cancellationToken = default)
    {
        if (!IsFeatureEnabled())
        {
            throw new InvalidOperationException("eCHIS integration is disabled by feature flag.");
        }

        var tenantId = _db.CurrentTenantId ?? TenantConstants.DefaultTenantId;
        var referralNumber = $"REF-OUT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var referral = new EchisReferral
        {
            TenantId = tenantId,
            ReferralNumber = referralNumber,
            Direction = EchisReferralDirection.Outbound,
            PatientId = input.PatientId,
            NationalPatientId = input.NationalPatientId,
            PatientFullName = input.PatientFullName,
            PatientPhone = input.PatientPhone,
            ReferringUnit = _options.Value.FacilityCode,
            ReceivingUnit = input.ReceivingUnit,
            ReasonForReferral = input.ReasonForReferral,
            Urgency = input.Urgency,
            Status = EchisReferralStatus.PendingTriage,
            ClinicalSummaryJson = input.ClinicalSummaryJson,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.EchisReferrals.Add(referral);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = new EchisReferralDto(
            ReferralNumber: referral.ReferralNumber,
            Direction: "Outbound",
            NationalPatientId: referral.NationalPatientId,
            PatientFullName: referral.PatientFullName,
            PatientPhone: referral.PatientPhone,
            ReferringUnit: referral.ReferringUnit,
            ReceivingUnit: referral.ReceivingUnit,
            ReasonForReferral: referral.ReasonForReferral,
            Urgency: referral.Urgency,
            Status: "PendingTriage",
            ClinicalSummaryJson: referral.ClinicalSummaryJson,
            CreatedAtUtc: referral.CreatedAtUtc
        );

        try
        {
            await _echisClient.SendReferralAsync(dto, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to immediately transmit outbound referral {RefNumber} to eCHIS. Saved locally for outbox sync.", referralNumber);
        }

        return referral;
    }

    public async Task<int> SyncInboundReferralsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeatureEnabled()) return 0;

        var tenantId = _db.CurrentTenantId ?? TenantConstants.DefaultTenantId;
        var inboundItems = await _echisClient.FetchInboundReferralsAsync(_options.Value.FacilityCode, cancellationToken);

        int importedCount = 0;
        foreach (var item in inboundItems)
        {
            var exists = await _db.EchisReferrals.AnyAsync(r => r.ReferralNumber == item.ReferralNumber, cancellationToken);
            if (exists) continue;

            var referral = new EchisReferral
            {
                TenantId = tenantId,
                ReferralNumber = item.ReferralNumber,
                Direction = EchisReferralDirection.Inbound,
                NationalPatientId = item.NationalPatientId,
                PatientFullName = item.PatientFullName,
                PatientPhone = item.PatientPhone,
                ReferringUnit = item.ReferringUnit,
                ReceivingUnit = item.ReceivingUnit,
                ReasonForReferral = item.ReasonForReferral,
                Urgency = item.Urgency,
                Status = EchisReferralStatus.PendingTriage,
                ClinicalSummaryJson = item.ClinicalSummaryJson,
                CreatedAtUtc = item.CreatedAtUtc
            };

            _db.EchisReferrals.Add(referral);
            importedCount++;
        }

        if (importedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Imported {Count} inbound community referrals from eCHIS.", importedCount);
        }

        return importedCount;
    }

    public async Task<bool> TriageReferralAsync(
        Guid referralId,
        EchisReferralStatus newStatus,
        string? triageNotes = null,
        CancellationToken cancellationToken = default)
    {
        var referral = await _db.EchisReferrals.FirstOrDefaultAsync(r => r.Id == referralId, cancellationToken);
        if (referral == null) return false;

        referral.Status = newStatus;
        referral.TriagedAtUtc = DateTime.UtcNow;
        referral.TriageNotes = triageNotes;

        await _db.SaveChangesAsync(cancellationToken);

        // Notify eCHIS gateway
        try
        {
            await _echisClient.UpdateReferralStatusAsync(referral.ReferralNumber, newStatus.ToString(), triageNotes, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to immediately transmit referral status update for {RefNumber} to eCHIS.", referral.ReferralNumber);
        }

        return true;
    }

    public async Task<IReadOnlyList<EchisReferral>> GetReferralsAsync(
        EchisReferralDirection? direction = null,
        EchisReferralStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.EchisReferrals.AsQueryable();

        if (direction.HasValue)
        {
            query = query.Where(r => r.Direction == direction.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        return await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken);
    }
}
