using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Hms;

public class HmsConflictService : IHmsConflictService
{
    private readonly MedicalPortalDbContext _db;
    private readonly ILogger<HmsConflictService> _logger;

    public HmsConflictService(MedicalPortalDbContext db, ILogger<HmsConflictService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<HmsConflictService>.Instance;
    }

    private record DemographicBaseline(
        string? FirstName,
        string? LastName,
        string? DateOfBirth,
        string? Address);

    public async Task<List<HmsDemographicConflict>> DetectAndReconcileDemographicsAsync(
        Patient patient,
        HmsPatientDto hmsPatient,
        CancellationToken cancellationToken = default)
    {
        var conflicts = new List<HmsDemographicConflict>();
        var person = patient.Person;
        if (person == null)
        {
            person = await _db.Persons.FirstOrDefaultAsync(p => p.Id == patient.PersonId, cancellationToken);
            if (person == null)
            {
                _logger.LogWarning("Cannot reconcile demographics: Patient {PatientId} has no associated Person.", patient.Id);
                return conflicts;
            }
        }

        // 1. Immediately update contact fields from HMS (per hms-decisions.md §1.3)
        if (!string.IsNullOrWhiteSpace(hmsPatient.Phone))
            person.Phone = hmsPatient.Phone;
        if (!string.IsNullOrWhiteSpace(hmsPatient.EmergencyContactName))
            person.EmergencyContactName = hmsPatient.EmergencyContactName;
        if (!string.IsNullOrWhiteSpace(hmsPatient.EmergencyContactPhone))
            person.EmergencyContactPhone = hmsPatient.EmergencyContactPhone;

        // 2. Parse last synced baseline
        DemographicBaseline? baseline = null;
        if (!string.IsNullOrWhiteSpace(patient.LastSyncedDemographicsJson))
        {
            try
            {
                baseline = JsonSerializer.Deserialize<DemographicBaseline>(patient.LastSyncedDemographicsJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse LastSyncedDemographicsJson for patient {PatientId}", patient.Id);
            }
        }

        var hmsDob = NormalizeDob(hmsPatient.DateOfBirth);
        var localDob = person.DateOfBirth.ToString("yyyy-MM-dd");

        // Helper to evaluate field conflict
        void CheckField(string fieldName, string? localVal, string? hmsVal, string? baseVal, Action<string> applyHms)
        {
            localVal = localVal?.Trim() ?? string.Empty;
            hmsVal = hmsVal?.Trim() ?? string.Empty;
            baseVal = baseVal?.Trim();

            if (string.Equals(localVal, hmsVal, StringComparison.OrdinalIgnoreCase))
            {
                // Match - no change / no conflict
                return;
            }

            if (baseVal != null && string.Equals(localVal, baseVal, StringComparison.OrdinalIgnoreCase))
            {
                // Local value still equals the last synced HMS value (local did not change, only HMS changed)
                // Apply the HMS value automatically without conflict
                applyHms(hmsVal);
                return;
            }

            if (baseVal == null && string.IsNullOrWhiteSpace(localVal))
            {
                // Initial sync and local is blank - populate from HMS
                applyHms(hmsVal);
                return;
            }

            // Both sides changed or local value diverges from HMS baseline - create conflict record
            var conflict = new HmsDemographicConflict
            {
                PatientId = patient.Id,
                HmsMrn = hmsPatient.GlobalId,
                FieldName = fieldName,
                LocalValue = localVal,
                HmsValue = hmsVal,
                LastSyncedValue = baseVal,
                DetectedAtUtc = DateTime.UtcNow,
                Status = HmsConflictStatus.Pending
            };

            _db.HmsDemographicConflicts.Add(conflict);
            conflicts.Add(conflict);
            _logger.LogInformation("Detected demographic conflict for patient {PatientId} on field '{Field}': recorded for administrative review",
                patient.Id, fieldName);
        }

        // Check identity and address fields
        CheckField("FirstName", person.FirstName, hmsPatient.FirstName, baseline?.FirstName, v => person.FirstName = v);
        CheckField("LastName", person.LastName, hmsPatient.LastName, baseline?.LastName, v => person.LastName = v);
        CheckField("DateOfBirth", localDob, hmsDob, baseline?.DateOfBirth, v =>
        {
            if (DateTime.TryParse(v, out var parsed))
                person.DateOfBirth = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        });
        CheckField("AddressLine1", person.AddressLine1, hmsPatient.Address, baseline?.Address, v => person.AddressLine1 = v);

        // Update baseline to current person values
        var newBaseline = new DemographicBaseline(
            person.FirstName,
            person.LastName,
            person.DateOfBirth.ToString("yyyy-MM-dd"),
            person.AddressLine1);

        patient.LastSyncedDemographicsJson = JsonSerializer.Serialize(newBaseline);
        await _db.SaveChangesAsync(cancellationToken);

        return conflicts;
    }

    public async Task<HmsDemographicConflict?> ResolveConflictAsync(
        Guid conflictId,
        bool acceptHms,
        Guid actorUserAccountId,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var conflict = await _db.HmsDemographicConflicts
            .Include(c => c.Patient).ThenInclude(p => p.Person)
            .FirstOrDefaultAsync(c => c.Id == conflictId, cancellationToken);

        if (conflict == null)
            return null;

        var person = conflict.Patient.Person;
        if (acceptHms && person != null && conflict.HmsValue != null)
        {
            switch (conflict.FieldName)
            {
                case "FirstName":
                    person.FirstName = conflict.HmsValue;
                    break;
                case "LastName":
                    person.LastName = conflict.HmsValue;
                    break;
                case "DateOfBirth":
                    if (DateTime.TryParse(conflict.HmsValue, out var dt))
                        person.DateOfBirth = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    break;
                case "AddressLine1":
                    person.AddressLine1 = conflict.HmsValue;
                    break;
            }

            // Update baseline
            var newBaseline = new DemographicBaseline(
                person.FirstName,
                person.LastName,
                person.DateOfBirth.ToString("yyyy-MM-dd"),
                person.AddressLine1);

            conflict.Patient.LastSyncedDemographicsJson = JsonSerializer.Serialize(newBaseline);
        }

        conflict.Status = acceptHms ? HmsConflictStatus.ResolvedAcceptHms : HmsConflictStatus.ResolvedKeepLocal;
        conflict.ResolvedByUserAccountId = actorUserAccountId;
        conflict.ResolvedAtUtc = DateTime.UtcNow;
        conflict.ResolutionNotes = notes;

        // Log audit trail
        _db.AuditLogs.Add(new AuditLog
        {
            Action = "HMS_DEMOGRAPHIC_CONFLICT_RESOLVED",
            ResourceType = "HmsDemographicConflict",
            ResourceId = conflict.Id.ToString(),
            ActorUserAccountId = actorUserAccountId,
            PatientId = conflict.PatientId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                conflictId = conflict.Id,
                field = conflict.FieldName,
                decision = acceptHms ? "ACCEPT_HMS" : "KEEP_LOCAL",
                localValue = conflict.LocalValue,
                hmsValue = conflict.HmsValue,
                notes
            })
        });

        await _db.SaveChangesAsync(cancellationToken);
        return conflict;
    }

    public Task<List<HmsDemographicConflict>> GetPendingConflictsAsync(
        Guid? patientId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.HmsDemographicConflicts
            .AsNoTracking()
            .Where(c => c.Status == HmsConflictStatus.Pending);

        if (patientId.HasValue)
        {
            query = query.Where(c => c.PatientId == patientId.Value);
        }

        return query.OrderByDescending(c => c.DetectedAtUtc).ToListAsync(cancellationToken);
    }

    private static string? NormalizeDob(string? dob)
    {
        if (string.IsNullOrWhiteSpace(dob))
            return null;
        if (DateTime.TryParse(dob, out var dt))
            return dt.ToString("yyyy-MM-dd");
        return dob.Trim();
    }
}
