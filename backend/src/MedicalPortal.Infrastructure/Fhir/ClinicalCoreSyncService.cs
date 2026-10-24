using System.Globalization;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Fhir;

public class ClinicalCoreSyncService : IClinicalCoreSyncService
{
    private readonly MedicalPortalDbContext _db;
    private readonly IClinicalCoreFhirClient _fhirClient;
    private readonly ISnapshotService _snapshotService;
    private readonly IOptions<ClinicalCoreFhirOptions> _options;
    private readonly ILogger<ClinicalCoreSyncService> _logger;

    public Exception? LastSyncException { get; set; }

    public ClinicalCoreSyncService(
        MedicalPortalDbContext db,
        IClinicalCoreFhirClient fhirClient,
        ISnapshotService snapshotService,
        IOptions<ClinicalCoreFhirOptions> options,
        ILogger<ClinicalCoreSyncService>? logger = null)
    {
        _db = db;
        _fhirClient = fhirClient;
        _snapshotService = snapshotService;
        _options = options;
        _logger = logger ?? NullLogger<ClinicalCoreSyncService>.Instance;
    }

    public async Task<bool> SyncPatientClinicalDataAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var patient = await _db.Patients
            .Include(p => p.Person)
            .Include(p => p.Observations)
            .Include(p => p.Allergies)
            .Include(p => p.Prescriptions).ThenInclude(pr => pr.Medication)
            .Include(p => p.Conditions)
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);

        if (patient == null)
        {
            _logger.LogWarning("Patient {PatientId} not found for clinical core sync.", patientId);
            return false;
        }

        try
        {
            // 1. Resolve FHIR Patient Identifier
            FhirPatientDto? fhirPatient = null;
            if (!string.IsNullOrWhiteSpace(patient.FhirPatientId))
            {
                fhirPatient = await _fhirClient.GetPatientByIdAsync(patient.FhirPatientId, cancellationToken);
            }
            if (fhirPatient == null && !string.IsNullOrWhiteSpace(patient.Mrn))
            {
                fhirPatient = await _fhirClient.GetPatientByMrnAsync(patient.Mrn, cancellationToken);
            }
            if (fhirPatient == null && !string.IsNullOrWhiteSpace(patient.HmsMrn))
            {
                fhirPatient = await _fhirClient.GetPatientByMrnAsync(patient.HmsMrn, cancellationToken);
            }

            if (fhirPatient == null)
            {
                Console.WriteLine($"[DEBUG] FHIR patient not found for MRN '{patient.Mrn}'.");
                _logger.LogInformation("No matching FHIR patient found in clinical core for portal Patient {PatientId} (MRN: {Mrn})", patientId, patient.Mrn);
                return false;
            }

            if (patient.FhirPatientId != fhirPatient.Id)
            {
                patient.FhirPatientId = fhirPatient.Id;
            }

            // 2. Observations (Labs & Vitals)
            var obsBundle = await _fhirClient.GetObservationsAsync(fhirPatient.Id, cancellationToken: cancellationToken);
            if (obsBundle?.Entry != null)
            {
                foreach (var entry in obsBundle.Entry)
                {
                    var fhirObs = entry.Resource;
                    if (fhirObs == null) continue;

                    var isVital = fhirObs.Category?.Any(c => c.Coding?.Any(cd => cd.Code == "vital-signs") == true) == true;
                    var category = isVital ? ObservationCategory.VITAL : ObservationCategory.LAB;

                    var code = fhirObs.Code?.Coding?.FirstOrDefault()?.Code ?? fhirObs.Code?.Text ?? fhirObs.Id;
                    var displayName = fhirObs.Code?.Coding?.FirstOrDefault()?.Display ?? fhirObs.Code?.Text ?? code;

                    DateTime observedAt = DateTime.UtcNow;
                    if (!string.IsNullOrWhiteSpace(fhirObs.EffectiveDateTime) &&
                        DateTime.TryParse(fhirObs.EffectiveDateTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDt))
                    {
                        observedAt = DateTime.SpecifyKind(parsedDt, DateTimeKind.Utc);
                    }

                    if (fhirObs.Component != null && fhirObs.Component.Count > 0)
                    {
                        foreach (var comp in fhirObs.Component)
                        {
                            var compCode = comp.Code?.Coding?.FirstOrDefault()?.Code ?? comp.Code?.Text ?? code;
                            var compName = comp.Code?.Coding?.FirstOrDefault()?.Display ?? comp.Code?.Text ?? displayName;
                            var compValNum = comp.ValueQuantity?.Value;
                            var compUnit = comp.ValueQuantity?.Unit;
                            var compValText = comp.ValueString ?? (compValNum.HasValue ? $"{compValNum} {compUnit}".Trim() : null);

                            var existingComp = patient.Observations.FirstOrDefault(o => o.Code == compCode && o.ObservedAt == observedAt);
                            if (existingComp == null)
                            {
                                var newComp = new Observation
                                {
                                    TenantId = patient.TenantId,
                                    PatientId = patient.Id,
                                    Category = category,
                                    Code = compCode,
                                    DisplayName = compName,
                                    ValueNumber = compValNum,
                                    ValueText = compValText,
                                    Unit = compUnit,
                                    ObservedAt = observedAt
                                };
                                _db.Observations.Add(newComp);
                            }
                            else
                            {
                                existingComp.ValueNumber = compValNum;
                                existingComp.ValueText = compValText;
                                existingComp.Unit = compUnit;
                            }
                        }
                    }
                    else
                    {
                        var valNum = fhirObs.ValueQuantity?.Value;
                        var unit = fhirObs.ValueQuantity?.Unit;
                        var valText = fhirObs.ValueString ?? (valNum.HasValue ? $"{valNum} {unit}".Trim() : null);

                        var existing = patient.Observations.FirstOrDefault(o => o.Code == code && o.ObservedAt == observedAt);
                        if (existing == null)
                        {
                            var newObs = new Observation
                            {
                                TenantId = patient.TenantId,
                                PatientId = patient.Id,
                                Category = category,
                                Code = code,
                                DisplayName = displayName,
                                ValueNumber = valNum,
                                ValueText = valText,
                                Unit = unit,
                                ObservedAt = observedAt
                            };
                            _db.Observations.Add(newObs);
                        }
                        else
                        {
                            existing.ValueNumber = valNum;
                            existing.ValueText = valText;
                            existing.Unit = unit;
                        }
                    }
                }
            }

            // 3. Allergies
            var allergiesBundle = await _fhirClient.GetAllergiesAsync(fhirPatient.Id, cancellationToken);
            if (allergiesBundle?.Entry != null)
            {
                foreach (var entry in allergiesBundle.Entry)
                {
                    var fhirAllergy = entry.Resource;
                    if (fhirAllergy == null) continue;

                    var substance = fhirAllergy.Code?.Text ?? fhirAllergy.Code?.Coding?.FirstOrDefault()?.Display ?? "Unknown Substance";
                    var drugClass = fhirAllergy.Category?.FirstOrDefault() ?? "medication";
                    var reaction = fhirAllergy.Reaction?.FirstOrDefault()?.Manifestation?.FirstOrDefault()?.Text
                                  ?? fhirAllergy.Reaction?.FirstOrDefault()?.Manifestation?.FirstOrDefault()?.Coding?.FirstOrDefault()?.Display
                                  ?? "Allergic reaction";

                    var severity = AllergySeverity.MODERATE;
                    var crit = fhirAllergy.Criticality?.ToLowerInvariant();
                    var reacSev = fhirAllergy.Reaction?.FirstOrDefault()?.Severity?.ToLowerInvariant();
                    if (crit == "high" || reacSev == "severe") severity = AllergySeverity.SEVERE;
                    else if (crit == "low" || reacSev == "mild") severity = AllergySeverity.MILD;

                    var isTombstone = fhirAllergy.ClinicalStatus?.Coding?.Any(c => c.Code == "inactive" || c.Code == "resolved") == true;

                    var existing = patient.Allergies.FirstOrDefault(a => a.Substance.Equals(substance, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var newAllergy = new Allergy
                        {
                            TenantId = patient.TenantId,
                            PatientId = patient.Id,
                            Substance = substance,
                            DrugClass = drugClass,
                            Reaction = reaction,
                            Severity = severity,
                            Tombstone = isTombstone,
                            RecordedAt = !string.IsNullOrWhiteSpace(fhirAllergy.RecordedDate) &&
                                         DateTime.TryParse(fhirAllergy.RecordedDate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var recDt)
                                ? DateTime.SpecifyKind(recDt, DateTimeKind.Utc)
                                : DateTime.UtcNow
                        };
                        _db.Allergies.Add(newAllergy);
                    }
                    else
                    {
                        existing.Reaction = reaction;
                        existing.Severity = severity;
                        existing.Tombstone = isTombstone;
                    }
                }
            }

            // 4. Medications (Prescriptions)
            var medsBundle = await _fhirClient.GetMedicationsAsync(fhirPatient.Id, cancellationToken);
            if (medsBundle?.Entry != null)
            {
                foreach (var entry in medsBundle.Entry)
                {
                    var fhirMed = entry.Resource;
                    if (fhirMed == null) continue;

                    var medName = fhirMed.MedicationCodeableConcept?.Text
                                  ?? fhirMed.MedicationCodeableConcept?.Coding?.FirstOrDefault()?.Display
                                  ?? "Medication";
                    var rxNorm = fhirMed.MedicationCodeableConcept?.Coding?.FirstOrDefault(c => c.System?.Contains("rxnorm", StringComparison.OrdinalIgnoreCase) == true)?.Code;
                    var dosage = fhirMed.DosageInstruction?.FirstOrDefault()?.Text ?? "As directed";
                    var isActive = fhirMed.Status?.Equals("active", StringComparison.OrdinalIgnoreCase) == true;

                    var medEntity = await _db.Medications.FirstOrDefaultAsync(m => m.Name.ToLower() == medName.ToLower(), cancellationToken)
                                    ?? _db.Medications.Local.FirstOrDefault(m => m.Name.ToLower() == medName.ToLower());
                    if (medEntity == null)
                    {
                        medEntity = new Medication
                        {
                            TenantId = patient.TenantId,
                            Name = medName,
                            RxNormCode = rxNorm
                        };
                        _db.Medications.Add(medEntity);
                    }

                    var existingPrescription = patient.Prescriptions.FirstOrDefault(p => p.Medication == medEntity || (p.MedicationId != Guid.Empty && p.MedicationId == medEntity.Id));
                    if (existingPrescription == null)
                    {
                        var prov = patient.PrimaryProvider 
                                   ?? await _db.Providers.FirstOrDefaultAsync(cancellationToken)
                                   ?? _db.Providers.Local.FirstOrDefault();

                        if (prov == null)
                        {
                            prov = new Provider
                            {
                                TenantId = patient.TenantId,
                                FirstName = "Clinical",
                                LastName = "Core",
                                Specialty = "General Medicine"
                            };
                            _db.Providers.Add(prov);
                        }

                        var newRx = new Prescription
                        {
                            TenantId = patient.TenantId,
                            PatientId = patient.Id,
                            Medication = medEntity,
                            Provider = prov,
                            Dosage = dosage,
                            Frequency = "Daily",
                            IsActive = isActive,
                            PrescribedAt = !string.IsNullOrWhiteSpace(fhirMed.AuthoredOn) &&
                                           DateTime.TryParse(fhirMed.AuthoredOn, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var authDt)
                                ? DateTime.SpecifyKind(authDt, DateTimeKind.Utc)
                                : DateTime.UtcNow
                        };
                        _db.Prescriptions.Add(newRx);
                    }
                    else
                    {
                        existingPrescription.Dosage = dosage;
                        existingPrescription.IsActive = isActive;
                    }
                }
            }

            // 5. Encounters -> Conditions / Problems
            var encBundle = await _fhirClient.GetEncountersAsync(fhirPatient.Id, cancellationToken);
            if (encBundle?.Entry != null)
            {
                foreach (var entry in encBundle.Entry)
                {
                    var enc = entry.Resource;
                    if (enc?.ReasonCode != null)
                    {
                        foreach (var reason in enc.ReasonCode)
                        {
                            var condCode = reason.Coding?.FirstOrDefault()?.Code ?? reason.Text ?? "CONDITION";
                            var condName = reason.Text ?? reason.Coding?.FirstOrDefault()?.Display ?? condCode;

                            var existing = patient.Conditions.FirstOrDefault(c => c.Code == condCode || c.Name.Equals(condName, StringComparison.OrdinalIgnoreCase));
                            if (existing == null)
                            {
                                var newCond = new Condition
                                {
                                    TenantId = patient.TenantId,
                                    PatientId = patient.Id,
                                    Code = condCode,
                                    Name = condName,
                                    ClinicalStatus = "ACTIVE",
                                    OnsetDate = !string.IsNullOrWhiteSpace(enc.Period?.Start) &&
                                                DateTime.TryParse(enc.Period.Start, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var encDt)
                                        ? DateTime.SpecifyKind(encDt, DateTimeKind.Utc)
                                        : DateTime.UtcNow
                                };
                                _db.Conditions.Add(newCond);
                            }
                        }
                    }
                }
            }

            patient.HmsLastSync = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            // 6. Refresh health snapshot from newly cached records
            await _snapshotService.RefreshSnapshotAsync(patient.Id);
            return true;
        }
        catch (Exception ex)
        {
            LastSyncException = ex;
            _logger.LogWarning(ex, "Clinical core FHIR synchronization failed for patient {PatientId}. Falling back cleanly to local DB cache.", patientId);
            return false;
        }
    }

    public async Task<int> SyncOutboundAppointmentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var unsyncedAppts = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Department)
                .Where(a => !a.HmsSynced && (a.Status == AppointmentStatus.CONFIRMED || a.Status == AppointmentStatus.REQUESTED))
                .ToListAsync(cancellationToken);

            int syncedCount = 0;
            foreach (var appt in unsyncedAppts)
            {
                var fhirPatientId = appt.Patient.FhirPatientId;
                if (string.IsNullOrWhiteSpace(fhirPatientId))
                {
                    var fhirPatient = await _fhirClient.GetPatientByMrnAsync(appt.Patient.HmsMrn ?? appt.Patient.Mrn, cancellationToken);
                    if (fhirPatient != null)
                    {
                        fhirPatientId = fhirPatient.Id;
                        appt.Patient.FhirPatientId = fhirPatient.Id;
                    }
                }

                if (string.IsNullOrWhiteSpace(fhirPatientId)) continue;

                var fhirAppt = new FhirAppointmentDto(
                    ResourceType: "Appointment",
                    Id: null,
                    Status: appt.Status == AppointmentStatus.CONFIRMED ? "booked" : "proposed",
                    ServiceCategory: appt.Department != null ? new List<FhirCodeableConceptDto>
                    {
                        new(new List<FhirCodingDto> { new("http://openmrs.org", appt.Department.Code ?? "GENERAL", appt.Department.Name) }, appt.Department.Name)
                    } : null,
                    Description: appt.ReasonForVisit ?? "Portal Consultation",
                    Start: appt.StartAt.ToString("o"),
                    End: appt.EndAt.ToString("o"),
                    Participant: new List<FhirAppointmentParticipantDto>
                    {
                        new(new FhirReferenceDto($"Patient/{fhirPatientId}", "Patient"), "accepted", "required")
                    },
                    Comment: appt.PatientNotes
                );

                var created = await _fhirClient.CreateAppointmentAsync(fhirAppt, cancellationToken);
                if (created != null)
                {
                    appt.HmsSynced = true;
                    syncedCount++;
                }
            }

            if (syncedCount > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            return syncedCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SyncOutboundAppointmentsAsync encountered an error. Continuing offline operations.");
            return 0;
        }
    }

    public async Task<int> SyncStalePatientsAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var stalePatients = await _db.Patients
                .Where(p => p.FhirPatientId != null || p.HmsMrn != null || p.Mrn != "")
                .OrderBy(p => p.HmsLastSync)
                .Take(limit)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            int success = 0;
            foreach (var pid in stalePatients)
            {
                if (await SyncPatientClinicalDataAsync(pid, cancellationToken))
                {
                    success++;
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SyncStalePatientsAsync encountered an error. Continuing offline operations.");
            return 0;
        }
    }

    public async Task<string?> GetO3PatientChartUrlAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);
        if (patient == null) return null;

        var fhirPatientId = patient.FhirPatientId;
        if (string.IsNullOrWhiteSpace(fhirPatientId))
        {
            try
            {
                var fhirPatient = await _fhirClient.GetPatientByMrnAsync(patient.HmsMrn ?? patient.Mrn, cancellationToken);
                if (fhirPatient != null)
                {
                    fhirPatientId = fhirPatient.Id;
                    patient.FhirPatientId = fhirPatient.Id;
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    fhirPatientId = patient.Mrn;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve FHIR patient ID from core for chart URL. Using MRN fallback.");
                fhirPatientId = patient.Mrn;
            }
        }

        var baseUrl = _options.Value.O3BaseUrl.TrimEnd('/');
        return $"{baseUrl}/patient/{fhirPatientId}/chart";
    }
}
