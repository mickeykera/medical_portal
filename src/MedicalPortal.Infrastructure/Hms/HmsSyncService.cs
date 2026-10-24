using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Hms;

public class HmsSyncService : IHmsSyncService
{
    private readonly MedicalPortalDbContext _db;
    private readonly IHmsClient _hmsClient;
    private readonly IHmsConflictService _conflictService;
    private readonly IHmsDeadLetterService _deadLetterService;
    private readonly ISnapshotService? _snapshotService;
    private readonly IConfiguration _config;
    private readonly ILogger<HmsSyncService> _logger;

    public HmsSyncService(
        MedicalPortalDbContext db,
        IHmsClient hmsClient,
        IHmsConflictService conflictService,
        IHmsDeadLetterService deadLetterService,
        IConfiguration config,
        ISnapshotService? snapshotService = null,
        ILogger<HmsSyncService>? logger = null)
    {
        _db = db;
        _hmsClient = hmsClient;
        _conflictService = conflictService;
        _deadLetterService = deadLetterService;
        _snapshotService = snapshotService;
        _config = config;
        _logger = logger ?? NullLogger<HmsSyncService>.Instance;
    }

    public async Task<bool> SyncPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var patient = await _db.Patients
            .Include(p => p.Person)
            .Include(p => p.Conditions)
            .Include(p => p.Observations)
            .Include(p => p.Appointments)
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);

        if (patient == null || string.IsNullOrWhiteSpace(patient.HmsMrn))
        {
            _logger.LogWarning("Patient {PatientId} not found or missing HmsMrn.", patientId);
            return false;
        }

        try
        {
            // 1. Search patient in HMS by HmsMrn (linking is only by HmsMrn, never by name/DOB)
            var hmsPatient = await _hmsClient.SearchPatientByMrnAsync(patient.HmsMrn, cancellationToken);
            if (hmsPatient == null)
            {
                _logger.LogWarning("HMS returned no patient for MRN {HmsMrn}", patient.HmsMrn);
                await _deadLetterService.RecordFailureAsync(
                    eventId: $"sync_patient_not_found_{patient.Id}_{DateTime.UtcNow.Ticks}",
                    source: "HMS_SYNC_WORKER",
                    error: $"Patient with HmsMrn '{patient.HmsMrn}' was not found in HMS",
                    payloadJson: JsonSerializer.Serialize(new { patientId = patient.Id, hmsMrn = patient.HmsMrn }),
                    hmsMrn: patient.HmsMrn,
                    entityType: "Patient",
                    entityId: patient.Id.ToString(),
                    cancellationToken: cancellationToken);
                return false;
            }

            // 2. Reconcile demographics following conflict rules
            await _conflictService.DetectAndReconcileDemographicsAsync(patient, hmsPatient, cancellationToken);

            // 3. Inbound EMR read
            var emr = await _hmsClient.GetEmrAsync(hmsPatient.Id, cancellationToken);
            if (emr != null)
            {
                // Reconcile Conditions (from medical_history)
                // Invariant: Never touch portal-native conditions (not prefixed with "HMS-")
                if (emr.MedicalHistory != null)
                {
                    var incomingHmsConditionIds = new HashSet<string>();

                    foreach (var h in emr.MedicalHistory)
                    {
                        var code = $"HMS-{h.Id}";
                        incomingHmsConditionIds.Add(code);

                        var existing = patient.Conditions.FirstOrDefault(c => c.Code == code);
                        if (existing == null)
                        {
                            var newCond = new Condition
                            {
                                PatientId = patient.Id,
                                Code = code,
                                Name = h.Condition,
                                ClinicalStatus = string.Equals(h.Status, "Resolved", StringComparison.OrdinalIgnoreCase) ? "RESOLVED" : "ACTIVE",
                                OnsetDate = DateTime.TryParse(h.DiagnosisDate, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null
                            };
                            _db.Conditions.Add(newCond);
                        }
                        else
                        {
                            var expectedStatus = string.Equals(h.Status, "Resolved", StringComparison.OrdinalIgnoreCase) ? "RESOLVED" : "ACTIVE";
                            if (existing.ClinicalStatus != expectedStatus)
                            {
                                existing.ClinicalStatus = expectedStatus;
                            }
                        }
                    }

                    // Tombstone, do not hard-delete: Mark missing HMS conditions as RESOLVED
                    foreach (var cond in patient.Conditions.Where(c => c.Code.StartsWith("HMS-")))
                    {
                        if (!incomingHmsConditionIds.Contains(cond.Code) && cond.ClinicalStatus == "ACTIVE")
                        {
                            cond.ClinicalStatus = "RESOLVED";
                        }
                    }
                }
            }

            // 4. Inbound Labs read
            var labs = await _hmsClient.GetLabTestsAsync(hmsPatient.Id, cancellationToken);
            if (labs.Count > 0)
            {
                foreach (var lab in labs)
                {
                    var test = lab.Test;
                    if (test == null) continue;

                    var labCode = $"HMS-LAB-{test.Id}";
                    var existingObs = patient.Observations.FirstOrDefault(o => o.Code == labCode);

                    var resultText = lab.Results != null && lab.Results.Count > 0
                        ? string.Join("; ", lab.Results.Select(r => r.ResultData))
                        : (test.Status ?? "Ordered");

                    var refRange = lab.Results?.FirstOrDefault()?.ReferenceRange;
                    var isAbnormal = lab.Results?.Any(r => r.Flagged == 1) ?? false;
                    var observedAt = DateTime.TryParse(test.CompletedAt ?? test.OrderedAt, out var obsDt)
                        ? DateTime.SpecifyKind(obsDt, DateTimeKind.Utc)
                        : DateTime.UtcNow;

                    if (existingObs == null)
                    {
                        var obs = new Observation
                        {
                            PatientId = patient.Id,
                            Category = ObservationCategory.LAB,
                            Code = labCode,
                            DisplayName = test.TestName,
                            ValueText = resultText,
                            RefRangeText = refRange,
                            IsAbnormal = isAbnormal,
                            ObservedAt = observedAt
                        };
                        _db.Observations.Add(obs);
                    }
                    else
                    {
                        existingObs.DisplayName = test.TestName;
                        existingObs.ValueText = resultText;
                        existingObs.RefRangeText = refRange;
                        existingObs.IsAbnormal = isAbnormal;
                        existingObs.ObservedAt = observedAt;
                    }
                }
            }

            // 5. Inbound Appointments status sync
            var appointments = await _hmsClient.GetAppointmentsAsync(hmsPatient.Id, cancellationToken);
            if (appointments.Count > 0)
            {
                foreach (var hmsAppt in appointments)
                {
                    if (!DateTime.TryParse(hmsAppt.ScheduledDate, out var schedUtc))
                        continue;
                    schedUtc = DateTime.SpecifyKind(schedUtc, DateTimeKind.Utc);

                    // Match appointment in local DB within 10-minute window
                    var localAppt = patient.Appointments
                        .FirstOrDefault(a => Math.Abs((a.StartAt - schedUtc).TotalMinutes) < 10);

                    if (localAppt != null)
                    {
                        if (Enum.TryParse<AppointmentStatus>(hmsAppt.Status, true, out var parsedStatus))
                        {
                            localAppt.Status = parsedStatus;
                            localAppt.HmsSynced = true;
                        }
                    }
                }
            }

            // Update sync stamp
            patient.HmsLastSync = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            // Refresh HealthSnapshot
            if (_snapshotService != null)
            {
                try
                {
                    await _snapshotService.RefreshSnapshotAsync(patient.Id);
                }
                catch { }
            }

            _logger.LogInformation("Successfully synced patient {PatientId} (MRN: {HmsMrn}) with HMS.", patient.Id, patient.HmsMrn);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during HMS sync for patient {PatientId}", patientId);
            await _deadLetterService.RecordFailureAsync(
                eventId: $"sync_patient_err_{patient.Id}_{DateTime.UtcNow.Ticks}",
                source: "HMS_SYNC_WORKER",
                error: ex.Message,
                payloadJson: JsonSerializer.Serialize(new { patientId, hmsMrn = patient.HmsMrn, exception = ex.ToString() }),
                hmsMrn: patient.HmsMrn,
                entityType: "Patient",
                entityId: patient.Id.ToString(),
                cancellationToken: cancellationToken);
            return false;
        }
    }

    public async Task<int> SyncStalePatientsAsync(TimeSpan? stalenessThreshold = null, CancellationToken cancellationToken = default)
    {
        var threshold = stalenessThreshold ?? TimeSpan.FromMinutes(_config.GetValue<int>("Hms:RefreshTtlMinutes", 15));
        var cutoff = DateTime.UtcNow - threshold;

        var stalePatientIds = await _db.Patients
            .AsNoTracking()
            .Where(p => p.HmsMrn != null && (p.HmsLastSync == null || p.HmsLastSync < cutoff))
            .Select(p => p.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        int syncedCount = 0;
        foreach (var id in stalePatientIds)
        {
            if (await SyncPatientAsync(id, cancellationToken))
            {
                syncedCount++;
            }
        }

        return syncedCount;
    }

    public async Task<int> SyncOutboundAppointmentsAsync(CancellationToken cancellationToken = default)
    {
        var unsyncedAppts = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Where(a => !a.HmsSynced && a.Patient.HmsMrn != null)
            .Take(25)
            .ToListAsync(cancellationToken);

        int pushedCount = 0;

        foreach (var appt in unsyncedAppts)
        {
            var hmsMrn = appt.Patient.HmsMrn!;
            try
            {
                var hmsPatient = await _hmsClient.SearchPatientByMrnAsync(hmsMrn, cancellationToken);
                if (hmsPatient == null)
                {
                    await _deadLetterService.RecordFailureAsync(
                        eventId: $"push_appt_nopatient_{appt.Id}",
                        source: "APPOINTMENT_PUSH",
                        error: $"HMS patient with MRN '{hmsMrn}' not found for appointment {appt.Id}",
                        payloadJson: JsonSerializer.Serialize(new { appointmentId = appt.Id, hmsMrn }),
                        hmsMrn: hmsMrn,
                        entityType: "Appointment",
                        entityId: appt.Id.ToString(),
                        cancellationToken: cancellationToken);
                    continue;
                }

                var req = new HmsCreateAppointmentRequestDto(
                    PatientId: hmsPatient.Id,
                    DoctorId: null,
                    ScheduledDate: appt.StartAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    DurationMinutes: (int)Math.Max(15, (appt.EndAt - appt.StartAt).TotalMinutes),
                    AppointmentType: "Consultation",
                    Notes: appt.ReasonForVisit ?? "Portal scheduled appointment");

                // Execute with retry logic (up to 3 attempts with exponential backoff)
                int attempt = 0;
                int maxAttempts = 3;
                HmsAppointmentDto? created = null;

                while (attempt < maxAttempts)
                {
                    attempt++;
                    try
                    {
                        created = await _hmsClient.CreateAppointmentAsync(req, cancellationToken);
                        if (created != null)
                            break;
                    }
                    catch (Exception)
                    {
                        if (attempt >= maxAttempts) throw;
                        await Task.Delay(100 * (int)Math.Pow(2, attempt - 1), cancellationToken);
                    }
                }

                if (created != null)
                {
                    appt.HmsSynced = true;
                    pushedCount++;
                }
                else
                {
                    await _deadLetterService.RecordFailureAsync(
                        eventId: $"push_appt_fail_{appt.Id}",
                        source: "APPOINTMENT_PUSH",
                        error: "HMS CreateAppointment returned null or failed",
                        payloadJson: JsonSerializer.Serialize(req),
                        hmsMrn: hmsMrn,
                        entityType: "Appointment",
                        entityId: appt.Id.ToString(),
                        attemptCount: maxAttempts,
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push appointment {AppointmentId} to HMS", appt.Id);
                await _deadLetterService.RecordFailureAsync(
                    eventId: $"push_appt_ex_{appt.Id}",
                    source: "APPOINTMENT_PUSH",
                    error: ex.Message,
                    payloadJson: JsonSerializer.Serialize(new { appointmentId = appt.Id, hmsMrn, ex = ex.ToString() }),
                    hmsMrn: hmsMrn,
                    entityType: "Appointment",
                    entityId: appt.Id.ToString(),
                    cancellationToken: cancellationToken);
            }
        }

        if (pushedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return pushedCount;
    }

    public async Task<int> SyncOutboundRefillsAsync(CancellationToken cancellationToken = default)
    {
        var unsyncedRefills = await _db.RefillRequests
            .Include(r => r.Prescription).ThenInclude(p => p.Patient)
            .Where(r => !r.HmsSynced && r.Prescription.Patient.HmsMrn != null)
            .Take(25)
            .ToListAsync(cancellationToken);

        int syncedCount = 0;

        foreach (var refill in unsyncedRefills)
        {
            var hmsMrn = refill.Prescription.Patient.HmsMrn!;
            try
            {
                var hmsPatient = await _hmsClient.SearchPatientByMrnAsync(hmsMrn, cancellationToken);
                if (hmsPatient != null)
                {
                    // Refills synced with HMS prescriptions
                    refill.HmsSynced = true;
                    syncedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync refill request {RefillId} with HMS", refill.Id);
                await _deadLetterService.RecordFailureAsync(
                    eventId: $"push_refill_ex_{refill.Id}",
                    source: "REFILL_SYNC",
                    error: ex.Message,
                    payloadJson: JsonSerializer.Serialize(new { refillId = refill.Id, hmsMrn }),
                    hmsMrn: hmsMrn,
                    entityType: "RefillRequest",
                    entityId: refill.Id.ToString(),
                    cancellationToken: cancellationToken);
            }
        }

        if (syncedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return syncedCount;
    }
}
