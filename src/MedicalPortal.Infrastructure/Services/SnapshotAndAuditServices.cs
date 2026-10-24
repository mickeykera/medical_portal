using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Infrastructure.Services;

public interface IAuditService
{
    Task LogAsync(
        string action,
        string resourceType,
        string? resourceId = null,
        Guid? actorUserAccountId = null,
        Guid? patientId = null,
        Guid? actingForPersonId = null,
        object? metadata = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool isProxy = false);

    Task EnqueueLogAsync(
        string action,
        string resourceType,
        string? resourceId = null,
        Guid? actorUserAccountId = null,
        Guid? patientId = null,
        Guid? actingForPersonId = null,
        object? metadata = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool isProxy = false);

    Task FlushAsync(CancellationToken cancellationToken = default);
}

public class AuditService : IAuditService
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditBatchQueue _queue;

    public AuditService(MedicalPortalDbContext db, IAuditBatchQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task LogAsync(
        string action,
        string resourceType,
        string? resourceId = null,
        Guid? actorUserAccountId = null,
        Guid? patientId = null,
        Guid? actingForPersonId = null,
        object? metadata = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool isProxy = false)
    {
        var log = new AuditLog
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ActorUserAccountId = actorUserAccountId,
            PatientId = patientId,
            ActingForPersonId = actingForPersonId,
            IsProxy = isProxy,
            MetadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }

    public async Task EnqueueLogAsync(
        string action,
        string resourceType,
        string? resourceId = null,
        Guid? actorUserAccountId = null,
        Guid? patientId = null,
        Guid? actingForPersonId = null,
        object? metadata = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool isProxy = false)
    {
        var log = new AuditLog
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ActorUserAccountId = actorUserAccountId,
            PatientId = patientId,
            ActingForPersonId = actingForPersonId,
            IsProxy = isProxy,
            MetadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow
        };

        await _queue.EnqueueAsync(log);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return _queue.FlushAsync(cancellationToken);
    }
}

public interface ISnapshotService
{
    Task<HealthSnapshot> RefreshSnapshotAsync(Guid patientId);
}

public class SnapshotService : ISnapshotService
{
    private readonly MedicalPortalDbContext _db;

    public SnapshotService(MedicalPortalDbContext db)
    {
        _db = db;
    }

    public async Task<HealthSnapshot> RefreshSnapshotAsync(Guid patientId)
    {
        var activeConditions = await _db.Conditions
            .AsNoTracking()
            .CountAsync(c => c.PatientId == patientId && c.ClinicalStatus == "ACTIVE");

        var activeMedications = await _db.Prescriptions
            .AsNoTracking()
            .CountAsync(p => p.PatientId == patientId && p.IsActive);

        var abnormalLabs = await _db.Observations
            .AsNoTracking()
            .CountAsync(o => o.PatientId == patientId && o.Category == ObservationCategory.LAB && o.IsAbnormal);

        var latestLabDate = await _db.Observations
            .AsNoTracking()
            .Where(o => o.PatientId == patientId && o.Category == ObservationCategory.LAB)
            .OrderByDescending(o => o.ObservedAt)
            .Select(o => (DateTime?)o.ObservedAt)
            .FirstOrDefaultAsync();

        var upcomingAppt = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patientId &&
                        a.StartAt >= DateTime.UtcNow &&
                        (a.Status == AppointmentStatus.CONFIRMED || a.Status == AppointmentStatus.REQUESTED))
            .OrderBy(a => a.StartAt)
            .Select(a => (DateTime?)a.StartAt)
            .FirstOrDefaultAsync();

        var totalBalance = await _db.Statements
            .AsNoTracking()
            .Where(s => s.PatientId == patientId && s.Status != StatementStatus.PAID && s.Status != StatementStatus.VOID)
            .SumAsync(s => s.BalanceCents);

        var snapshot = await _db.HealthSnapshots.FindAsync(patientId);
        if (snapshot == null)
        {
            snapshot = new HealthSnapshot { PatientId = patientId };
            _db.HealthSnapshots.Add(snapshot);
        }

        snapshot.GeneratedAt = DateTime.UtcNow;
        snapshot.ActiveConditionCount = activeConditions;
        snapshot.ActiveMedicationCount = activeMedications;
        snapshot.AbnormalLabCount = abnormalLabs;
        snapshot.ImmunizationStatus = "UP_TO_DATE";
        snapshot.LatestLabObservedAt = latestLabDate;
        snapshot.UpcomingAppointmentAt = upcomingAppt;
        snapshot.OutstandingBalanceCents = totalBalance;

        await _db.SaveChangesAsync();
        return snapshot;
    }
}
