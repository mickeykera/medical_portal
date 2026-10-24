using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Fhir;
using MedicalPortal.Infrastructure.Observability;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/clinical")]
[RequireAuth]
[EnableRateLimiting("GeneralApiPolicy")]
public class ClinicalController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly ISnapshotService _snapshotService;
    private readonly IAccessControlService _accessControl;
    private readonly IAuditService _audit;
    private readonly IInteractionKnowledgeBase _interactionKb;
    private readonly IAllergyKnowledgeBase _allergyKb;
    private readonly IClinicalCoreSyncService? _coreSyncService;
    private readonly IClinicalCoreFhirClient? _fhirClient;

    public ClinicalController(
        MedicalPortalDbContext db,
        ISnapshotService snapshotService,
        IAccessControlService accessControl,
        IAuditService audit,
        IInteractionKnowledgeBase? interactionKb = null,
        IAllergyKnowledgeBase? allergyKb = null,
        IClinicalCoreSyncService? coreSyncService = null,
        IClinicalCoreFhirClient? fhirClient = null)
    {
        _db = db;
        _snapshotService = snapshotService;
        _accessControl = accessControl;
        _audit = audit;
        _interactionKb = interactionKb ?? DrugInteractionLogic.DefaultKnowledgeBase;
        _allergyKb = allergyKb ?? AllergyLogic.DefaultKnowledgeBase;
        _coreSyncService = coreSyncService;
        _fhirClient = fhirClient;
    }

    private Guid TargetPatientId => (Guid)HttpContext.Items["TargetPatientId"]!;
    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;

    /// <summary>
    /// Deep-links clinician encounter charting to the OpenMRS O3 microfrontend SPA.
    /// In target architecture B &amp; C, clinician charting moves to the core's UI.
    /// </summary>
    [HttpGet("o3-chart-url")]
    [RequireClinician]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalNotes, AppAction.Read)]
    public async Task<IActionResult> GetO3ChartUrl([FromQuery] Guid? patientId = null)
    {
        var targetId = patientId ?? TargetPatientId;
        if (_coreSyncService == null)
        {
            return StatusCode(503, new { error = "Clinical Core sync service is not available." });
        }

        var chartUrl = await _coreSyncService.GetO3PatientChartUrlAsync(targetId);
        if (string.IsNullOrWhiteSpace(chartUrl))
        {
            return NotFound(new { error = "Clinical core chart URL could not be resolved for patient." });
        }

        return Ok(new
        {
            patientId = targetId,
            chartUrl,
            provider = "OpenMRS O3 Microfrontend",
            message = "Clinician encounter charting and ordering is mastered in the Clinical Core UI."
        });
    }

    [HttpGet("snapshot")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read)]
    public async Task<IActionResult> GetSnapshot()
    {
        var patientId = TargetPatientId;
        if (_coreSyncService != null)
        {
            await _coreSyncService.SyncPatientClinicalDataAsync(patientId);
        }
        var snapshot = await _snapshotService.RefreshSnapshotAsync(patientId);

        var canSeeBalance = (await _accessControl.ResolveAccessAsync(Actor, patientId, Permission.payBills)).Allowed;

        return Ok(new
        {
            generatedAt = snapshot.GeneratedAt.ToString("o"),
            activeConditionCount = snapshot.ActiveConditionCount,
            activeMedicationCount = snapshot.ActiveMedicationCount,
            abnormalLabCount = snapshot.AbnormalLabCount,
            immunizationStatus = snapshot.ImmunizationStatus,
            latestLabObservedAt = snapshot.LatestLabObservedAt?.ToString("o"),
            upcomingAppointmentAt = snapshot.UpcomingAppointmentAt?.ToString("o"),
            outstandingBalance = canSeeBalance ? new
            {
                cents = snapshot.OutstandingBalanceCents,
                formatted = ClinicalFormatting.FormatCents(snapshot.OutstandingBalanceCents)
            } : null
        });
    }

    [HttpGet("labs")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read)]
    public async Task<IActionResult> GetLabs()
    {
        if (_coreSyncService != null)
        {
            await _coreSyncService.SyncPatientClinicalDataAsync(TargetPatientId);
        }

        var labs = await _db.Observations
            .AsNoTracking()
            .Where(o => o.PatientId == TargetPatientId && o.Category == ObservationCategory.LAB)
            .OrderByDescending(o => o.ObservedAt)
            .ToListAsync();

        return Ok(new
        {
            results = labs.Select(l =>
            {
                string? direction = null;
                if (l.ValueNumber.HasValue)
                {
                    if (l.RefRangeHigh.HasValue && l.ValueNumber > l.RefRangeHigh) direction = "HIGH";
                    else if (l.RefRangeLow.HasValue && l.ValueNumber < l.RefRangeLow) direction = "LOW";
                    else if (l.IsAbnormal) direction = "OUT_OF_RANGE";
                }
                else if (l.IsAbnormal)
                {
                    direction = "OUT_OF_RANGE";
                }

                return new
                {
                    id = l.Id,
                    code = l.Code,
                    name = l.DisplayName,
                    value = l.ValueNumber.HasValue ? $"{l.ValueNumber} {l.Unit}".Trim() : (l.ValueText ?? "—"),
                    referenceRange = l.RefRangeText,
                    direction,
                    isAbnormal = l.IsAbnormal,
                    observedAt = l.ObservedAt.ToString("o")
                };
            })
        });
    }

    [HttpGet("allergies")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read)]
    public async Task<IActionResult> GetAllergies()
    {
        if (_coreSyncService != null)
        {
            await _coreSyncService.SyncPatientClinicalDataAsync(TargetPatientId);
        }

        var allergies = await _db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == TargetPatientId && !a.Tombstone)
            .OrderByDescending(a => a.RecordedAt)
            .ToListAsync();

        return Ok(new
        {
            allergies = allergies.Select(a => new
            {
                id = a.Id,
                substance = a.Substance,
                drugClass = a.DrugClass,
                drugClassCode = a.DrugClassCode,
                reaction = a.Reaction,
                severity = a.Severity.ToString(),
                recordedAt = a.RecordedAt.ToString("o")
            })
        });
    }

    [HttpGet("vitals")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read)]
    public async Task<IActionResult> GetVitals()
    {
        if (_coreSyncService != null)
        {
            await _coreSyncService.SyncPatientClinicalDataAsync(TargetPatientId);
        }

        var vitals = await _db.Observations
            .AsNoTracking()
            .Where(o => o.PatientId == TargetPatientId && o.Category == ObservationCategory.VITAL)
            .OrderByDescending(o => o.ObservedAt)
            .ToListAsync();

        // One reading per distinct code (most recent)
        var latestByCode = vitals
            .GroupBy(v => v.Code)
            .Select(g => g.First())
            .ToList();

        return Ok(new
        {
            vitals = latestByCode.Select(v => new
            {
                id = v.Id,
                code = v.Code,
                label = v.DisplayName,
                value = v.ValueNumber.HasValue ? v.ValueNumber.Value.ToString() : (v.ValueText ?? "—"),
                unit = v.Unit,
                referenceRange = v.RefRangeText,
                isAbnormal = v.IsAbnormal,
                direction = v.IsAbnormal ? (v.RefRangeHigh.HasValue && v.ValueNumber > v.RefRangeHigh ? "HIGH" : (v.RefRangeLow.HasValue && v.ValueNumber < v.RefRangeLow ? "LOW" : "OUT_OF_RANGE")) : null,
                observedAt = v.ObservedAt.ToString("o")
            })
        });
    }

    [HttpGet("medications")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.Medications, AppAction.Read)]
    public async Task<IActionResult> GetMedications()
    {
        if (_coreSyncService != null)
        {
            await _coreSyncService.SyncPatientClinicalDataAsync(TargetPatientId);
        }

        var prescriptions = await _db.Prescriptions
            .AsNoTracking()
            .Include(p => p.Medication)
            .Where(p => p.PatientId == TargetPatientId && p.IsActive)
            .OrderByDescending(p => p.PrescribedAt)
            .ToListAsync();

        return Ok(new
        {
            medications = prescriptions.Select(p => new
            {
                id = p.Id,
                name = p.Medication.Name,
                strength = p.Medication.Strength,
                form = p.Medication.Form,
                dosage = p.Dosage,
                frequency = p.Frequency,
                instructions = p.Instructions,
                expiresAt = p.ExpiresAt?.ToString("o")
            })
        });
    }

    [HttpGet("conditions")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read)]
    public async Task<IActionResult> GetConditions()
    {
        if (_coreSyncService != null)
        {
            await _coreSyncService.SyncPatientClinicalDataAsync(TargetPatientId);
        }

        var conditions = await _db.Conditions
            .AsNoTracking()
            .Where(c => c.PatientId == TargetPatientId)
            .OrderByDescending(c => c.OnsetDate)
            .ToListAsync();

        return Ok(new
        {
            conditions = conditions.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                clinicalStatus = c.ClinicalStatus,
                onsetDate = c.OnsetDate.HasValue ? ClinicalFormatting.ToIsoDate(c.OnsetDate) : null
            })
        });
    }

    [HttpGet("procedures")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read)]
    public async Task<IActionResult> GetProcedures()
    {
        var procedures = await _db.Procedures
            .AsNoTracking()
            .Where(p => p.PatientId == TargetPatientId)
            .OrderByDescending(p => p.PerformedAt)
            .ToListAsync();

        return Ok(new
        {
            procedures = procedures.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                performedAt = p.PerformedAt.ToString("o"),
                performedBy = p.PerformedBy,
                facility = p.Facility
            })
        });
    }

    [HttpGet("immunizations")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read)]
    public async Task<IActionResult> GetImmunizations()
    {
        var immunizations = await _db.Observations
            .AsNoTracking()
            .Where(o => o.PatientId == TargetPatientId && o.Category == ObservationCategory.IMMUNIZATION)
            .OrderByDescending(o => o.ObservedAt)
            .ToListAsync();

        return Ok(new
        {
            immunizations = immunizations.Select(i => new
            {
                id = i.Id,
                name = i.DisplayName,
                status = i.ImmunizationStatus ?? "COMPLETED",
                administeredAt = i.ObservedAt.ToString("o"),
                lotNumber = i.LotNumber
            })
        });
    }

    [HttpGet("trends")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalResults, AppAction.Read)]
    public async Task<IActionResult> GetTrends()
    {
        var observations = await _db.Observations
            .AsNoTracking()
            .Where(o => o.PatientId == TargetPatientId && o.Category == ObservationCategory.LAB)
            .OrderBy(o => o.ObservedAt)
            .ToListAsync();

        var grouped = observations.GroupBy(o => o.Code);
        var seriesList = new List<object>();

        foreach (var group in grouped)
        {
            var points = group.Select(o => new
            {
                valueNumber = o.ValueNumber,
                valueText = o.ValueText,
                isAbnormal = o.IsAbnormal,
                observedAt = o.ObservedAt.ToString("o")
            }).ToList();

            var numericPoints = points.Where(p => p.valueNumber.HasValue).ToList();
            decimal? first = numericPoints.FirstOrDefault()?.valueNumber;
            decimal? last = numericPoints.LastOrDefault()?.valueNumber;
            decimal? delta = (first.HasValue && last.HasValue) ? last.Value - first.Value : null;

            var direction = "unknown";
            if (delta.HasValue)
            {
                if (delta.Value > 0.1m) direction = "rising";
                else if (delta.Value < -0.1m) direction = "falling";
                else direction = "steady";
            }

            var sample = group.Last();
            var abnormalShare = points.Count > 0 ? (double)points.Count(p => p.isAbnormal) / points.Count : 0.0;
            var latestAbnormal = sample.IsAbnormal;
            var worthFlagging = latestAbnormal || abnormalShare >= 0.5;

            seriesList.Add(new
            {
                code = group.Key,
                name = sample.DisplayName,
                unit = sample.Unit,
                refRangeLow = sample.RefRangeLow,
                refRangeHigh = sample.RefRangeHigh,
                first,
                last,
                delta,
                direction,
                abnormalShare,
                latestAbnormal,
                worthFlagging,
                points
            });
        }

        return Ok(new
        {
            since = DateTime.UtcNow.AddYears(-2).ToString("o"),
            series = seriesList
        });
    }

    public record PublishObservationRequest(
        string Code,
        string DisplayName,
        decimal? ValueNumber,
        string? ValueText,
        string? Unit,
        decimal? RefRangeLow,
        decimal? RefRangeHigh,
        string ObservedAt);

    [HttpPost("observations")]
    [RequireClinician]
    public async Task<IActionResult> PublishObservation([FromBody] PublishObservationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { error = "Code and DisplayName are required." });

        if (!DateTime.TryParse(request.ObservedAt, out var obsAt))
            return BadRequest(new { error = "Valid observedAt date is required." });

        bool isAbnormal = false;
        if (request.ValueNumber.HasValue)
        {
            if (request.RefRangeHigh.HasValue && request.ValueNumber.Value > request.RefRangeHigh.Value) isAbnormal = true;
            if (request.RefRangeLow.HasValue && request.ValueNumber.Value < request.RefRangeLow.Value) isAbnormal = true;
        }

        var obs = new Observation
        {
            PatientId = TargetPatientId,
            Category = ObservationCategory.LAB,
            Code = request.Code.Trim(),
            DisplayName = request.DisplayName.Trim(),
            ValueNumber = request.ValueNumber,
            ValueText = request.ValueText,
            Unit = request.Unit,
            RefRangeLow = request.RefRangeLow,
            RefRangeHigh = request.RefRangeHigh,
            RefRangeText = (request.RefRangeLow.HasValue && request.RefRangeHigh.HasValue) ? $"{request.RefRangeLow}-{request.RefRangeHigh} {request.Unit}".Trim() : null,
            IsAbnormal = isAbnormal,
            ObservedAt = DateTime.SpecifyKind(obsAt, DateTimeKind.Utc)
        };

        _db.Observations.Add(obs);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OBSERVATION_PUBLISHED", "Observation", obs.Id.ToString(), Actor.UserAccountId, TargetPatientId);
        await _snapshotService.RefreshSnapshotAsync(TargetPatientId);

        return StatusCode(201, new
        {
            id = obs.Id,
            code = obs.Code,
            name = obs.DisplayName,
            value = obs.ValueNumber.HasValue ? $"{obs.ValueNumber} {obs.Unit}".Trim() : (obs.ValueText ?? "—"),
            referenceRange = obs.RefRangeText,
            isAbnormal = obs.IsAbnormal,
            observedAt = obs.ObservedAt.ToString("o")
        });
    }

    public record SafetyCheckRequest(string MedicationName, string? RxNormCode = null);
    public record PrescribeRequest(
        string MedicationName,
        string? Strength,
        string? Form,
        string Dosage,
        string Frequency,
        string? Instructions,
        string? OverrideJustification,
        string? RxNormCode = null);

    [HttpPost("prescriptions/safety-check")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.Medications, AppAction.Read)]
    public async Task<IActionResult> CheckPrescriptionSafety([FromBody] SafetyCheckRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MedicationName))
            return BadRequest(new { error = "MedicationName is required." });

        var patientId = TargetPatientId;

        // Resolve or enrich RxNorm code for the proposed medication
        var proposedRxNorm = request.RxNormCode;
        if (string.IsNullOrWhiteSpace(proposedRxNorm))
        {
            var knownMedRx = await _db.Medications
                .Where(m => m.Name.ToLower() == request.MedicationName.Trim().ToLower() && m.RxNormCode != null)
                .Select(m => m.RxNormCode)
                .FirstOrDefaultAsync();
            proposedRxNorm = knownMedRx ?? _interactionKb.ResolveRxNormCode(request.MedicationName);
        }

        var proposedConcept = new MedicationConcept(request.MedicationName.Trim(), proposedRxNorm);

        // 1. Check Allergies
        var patientAllergies = await _db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .ToListAsync();

        var allergyWarnings = _allergyKb.CheckContraindications(
            patientAllergies,
            proposedConcept
        );

        // 2. Check Drug-Drug Interactions
        var activeMeds = await _db.Prescriptions
            .AsNoTracking()
            .Include(p => p.Medication)
            .Where(p => p.PatientId == patientId && p.IsActive)
            .Select(p => new MedicationConcept(p.Medication.Name, p.Medication.RxNormCode, null))
            .ToListAsync();

        var interactionWarnings = _interactionKb.CheckInteractions(
            activeMeds,
            proposedConcept
        );

        var hasHighSeverity = allergyWarnings.Any(w => w.Severity is AllergySeverity.SEVERE or AllergySeverity.ANAPHYLAXIS) ||
                              interactionWarnings.Any(w => w.Severity == InteractionSeverity.HIGH);

        // Record compliance audit log documenting the evaluation and knowledge base versions
        using (var activity = MedicalPortalDiagnostics.ActivitySource.StartActivity("portal.clinical.safety_check"))
        {
            activity?.SetTag("medication.name", request.MedicationName);
            activity?.SetTag("medication.rxnorm", proposedConcept.RxNormCode);
            MedicalPortalDiagnostics.ClinicalSafetyChecksTotal.Add(1);
        }

        await _audit.LogAsync(
            "CLINICAL_SAFETY_CHECK",
            "PrescriptionSafety",
            patientId.ToString(),
            Actor.UserAccountId,
            patientId,
            metadata: new
            {
                medicationName = request.MedicationName,
                rxNormCode = proposedConcept.RxNormCode,
                interactionKnowledgeBaseVersion = _interactionKb.Version,
                allergyKnowledgeBaseVersion = _allergyKb.Version,
                interactionKnowledgeBaseProvider = _interactionKb.ProviderName,
                allergyKnowledgeBaseProvider = _allergyKb.ProviderName,
                safe = allergyWarnings.Count == 0 && interactionWarnings.Count == 0,
                hasAllergyConflict = allergyWarnings.Count > 0,
                allergyWarningCount = allergyWarnings.Count,
                hasDrugInteraction = interactionWarnings.Count > 0,
                drugInteractionCount = interactionWarnings.Count,
                requiresOverride = hasHighSeverity,
                disclaimer = _interactionKb.Disclaimer
            });

        return Ok(new
        {
            medicationName = request.MedicationName,
            rxNormCode = proposedConcept.RxNormCode,
            safe = allergyWarnings.Count == 0 && interactionWarnings.Count == 0,
            hasAllergyConflict = allergyWarnings.Count > 0,
            allergyWarnings = allergyWarnings.Select(w => new
            {
                severity = w.Severity.ToString(),
                substance = w.Substance,
                reason = w.Reason,
                reaction = w.Reaction
            }),
            hasDrugInteraction = interactionWarnings.Count > 0,
            drugInteractions = interactionWarnings.Select(w => new
            {
                severity = w.Severity.ToString(),
                drug1 = w.Drug1,
                drug2 = w.Drug2,
                description = w.Description,
                clinicalGuidance = w.ClinicalGuidance
            }),
            requiresOverride = hasHighSeverity,
            knowledgeBases = new
            {
                interactionVersion = _interactionKb.Version,
                allergyVersion = _allergyKb.Version,
                provider = _interactionKb.ProviderName,
                disclaimer = "DEMO REFERENCE ONLY: This safety screening was evaluated against sample demonstration rules. It does not claim clinical completeness or exhaustiveness."
            }
        });
    }

    [HttpPost("prescriptions")]
    [RequireClinician]
    [RequirePatientAccess(Permission.viewRecords, AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> PrescribeMedication([FromBody] PrescribeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MedicationName))
            return BadRequest(new { error = "MedicationName is required." });

        var patientId = TargetPatientId;

        // Resolve or enrich RxNorm code
        var proposedRxNorm = request.RxNormCode;
        if (string.IsNullOrWhiteSpace(proposedRxNorm))
        {
            var knownMedRx = await _db.Medications
                .Where(m => m.Name.ToLower() == request.MedicationName.Trim().ToLower() && m.RxNormCode != null)
                .Select(m => m.RxNormCode)
                .FirstOrDefaultAsync();
            proposedRxNorm = knownMedRx ?? _interactionKb.ResolveRxNormCode(request.MedicationName);
        }

        var proposedConcept = new MedicationConcept(request.MedicationName.Trim(), proposedRxNorm);

        // Safety verification using pluggable knowledge bases
        var allergies = await _db.Allergies.AsNoTracking().Where(a => a.PatientId == patientId).ToListAsync();
        var allergyWarnings = _allergyKb.CheckContraindications(allergies, proposedConcept);

        var activeMeds = await _db.Prescriptions.AsNoTracking().Include(p => p.Medication)
            .Where(p => p.PatientId == patientId && p.IsActive)
            .Select(p => new MedicationConcept(p.Medication.Name, p.Medication.RxNormCode, null)).ToListAsync();
        var drugInteractions = _interactionKb.CheckInteractions(activeMeds, proposedConcept);

        var hasSevereRisk = allergyWarnings.Any(w => w.Severity is AllergySeverity.SEVERE or AllergySeverity.ANAPHYLAXIS) ||
                            drugInteractions.Any(w => w.Severity == InteractionSeverity.HIGH);

        if (hasSevereRisk && string.IsNullOrWhiteSpace(request.OverrideJustification))
        {
            return Conflict(new
            {
                error = "Prescription blocked due to high-severity clinical contraindication.",
                allergyWarnings,
                drugInteractions,
                requiresOverrideJustification = true,
                knowledgeBases = new
                {
                    interactionVersion = _interactionKb.Version,
                    allergyVersion = _allergyKb.Version
                }
            });
        }

        var med = await _db.Medications.FirstOrDefaultAsync(m => m.Name.ToLower() == request.MedicationName.ToLower());
        if (med == null)
        {
            med = new Medication
            {
                Name = request.MedicationName.Trim(),
                Strength = request.Strength?.Trim() ?? "Standard",
                Form = request.Form?.Trim() ?? "Oral Tablet",
                RxNormCode = proposedRxNorm
            };
            _db.Medications.Add(med);
            await _db.SaveChangesAsync();
        }
        else if (string.IsNullOrWhiteSpace(med.RxNormCode) && !string.IsNullOrWhiteSpace(proposedRxNorm))
        {
            med.RxNormCode = proposedRxNorm;
            await _db.SaveChangesAsync();
        }

        var provider = await _db.Providers.FirstOrDefaultAsync(p => p.AccountId == Actor.UserAccountId);
        var providerId = provider?.Id ?? await _db.Providers.Select(p => p.Id).FirstOrDefaultAsync();

        var rx = new Prescription
        {
            PatientId = patientId,
            MedicationId = med.Id,
            ProviderId = providerId,
            Dosage = request.Dosage,
            Frequency = request.Frequency,
            Instructions = request.Instructions ?? "As directed by physician",
            IsActive = true,
            PrescribedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddYears(1)
        };

        _db.Prescriptions.Add(rx);
        await _db.SaveChangesAsync();

        if (hasSevereRisk && !string.IsNullOrWhiteSpace(request.OverrideJustification))
        {
            await _audit.LogAsync("SAFETY_CONTRAINDICATION_OVERRIDDEN", "Prescription", rx.Id.ToString(), Actor.UserAccountId, patientId, 
                metadata: new
                {
                    overrideReason = request.OverrideJustification,
                    interactionKnowledgeBaseVersion = _interactionKb.Version,
                    allergyKnowledgeBaseVersion = _allergyKb.Version,
                    interactionKnowledgeBaseProvider = _interactionKb.ProviderName,
                    allergyKnowledgeBaseProvider = _allergyKb.ProviderName,
                    medicationName = med.Name,
                    rxNormCode = med.RxNormCode
                });
        }

        await _audit.LogAsync("PRESCRIPTION_ISSUED", "Prescription", rx.Id.ToString(), Actor.UserAccountId, patientId,
            metadata: new
            {
                medicationName = med.Name,
                rxNormCode = med.RxNormCode,
                interactionKnowledgeBaseVersion = _interactionKb.Version,
                allergyKnowledgeBaseVersion = _allergyKb.Version
            });
        await _snapshotService.RefreshSnapshotAsync(patientId);

        return StatusCode(201, new
        {
            id = rx.Id,
            medicationName = med.Name,
            strength = med.Strength,
            dosage = rx.Dosage,
            frequency = rx.Frequency,
            instructions = rx.Instructions,
            prescribedAt = rx.PrescribedAt.ToString("o")
        });
    }

    [HttpGet("summary/export")]
    [RequirePatientAccess(Permission.viewRecords, AppResource.ClinicalNotes, AppAction.Read)]
    public async Task<IActionResult> ExportVisitSummary()
    {
        var patientId = TargetPatientId;
        var patient = await _db.Patients
            .Include(p => p.Person)
            .Include(p => p.PrimaryProvider)
            .FirstOrDefaultAsync(p => p.Id == patientId);

        if (patient == null) return NotFound();

        var conditions = await _db.Conditions.AsNoTracking().Where(c => c.PatientId == patientId).ToListAsync();
        var allergies = await _db.Allergies.AsNoTracking().Where(a => a.PatientId == patientId).ToListAsync();
        var meds = await _db.Prescriptions.AsNoTracking().Include(p => p.Medication).Where(p => p.PatientId == patientId && p.IsActive).ToListAsync();
        var vitals = await _db.Observations.AsNoTracking().Where(o => o.PatientId == patientId).OrderByDescending(o => o.ObservedAt).Take(15).ToListAsync();

        var html = ClinicalSummaryGenerator.GenerateHtml(patient, conditions, allergies, meds, vitals);
        return Content(html, "text/html; charset=utf-8");
    }
}
