using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Infrastructure.Services;

public interface IFhirService
{
    object GetCapabilityStatement();
    Task<object?> GetPatientFhirAsync(Guid patientId);
    Task<object> GetPatientObservationsAsync(Guid patientId);
    Task<object> GetPatientAllergiesAsync(Guid patientId);
    Task<object> GetPatientConditionsAsync(Guid patientId);
    Task<object> GetPatientMedicationsAsync(Guid patientId);
    Task<object> GetPatientAppointmentsAsync(Guid patientId);
    Task<object?> GetPatientEverythingBundleAsync(Guid patientId);
}

public class FhirService : IFhirService
{
    private readonly MedicalPortalDbContext _db;

    public FhirService(MedicalPortalDbContext db)
    {
        _db = db;
    }

    public object GetCapabilityStatement()
    {
        return new
        {
            resourceType = "CapabilityStatement",
            status = "active",
            date = DateTime.UtcNow.ToString("o"),
            kind = "instance",
            fhirVersion = "4.0.1",
            format = new[] { "application/fhir+json", "application/json" },
            rest = new[]
            {
                new
                {
                    mode = "server",
                    resource = new[]
                    {
                        new { type = "Patient", interaction = new[] { new { code = "read" }, new { code = "search-type" } } },
                        new { type = "Observation", interaction = new[] { new { code = "read" }, new { code = "search-type" } } },
                        new { type = "Condition", interaction = new[] { new { code = "read" }, new { code = "search-type" } } },
                        new { type = "AllergyIntolerance", interaction = new[] { new { code = "read" }, new { code = "search-type" } } },
                        new { type = "MedicationRequest", interaction = new[] { new { code = "read" }, new { code = "search-type" } } },
                        new { type = "Appointment", interaction = new[] { new { code = "read" }, new { code = "search-type" } } }
                    }
                }
            }
        };
    }

    public async Task<object?> GetPatientFhirAsync(Guid patientId)
    {
        var patient = await _db.Patients
            .AsNoTracking()
            .Include(p => p.Person)
            .FirstOrDefaultAsync(p => p.Id == patientId);

        if (patient == null) return null;
        return MapPatientToFhir(patient);
    }

    public async Task<object> GetPatientObservationsAsync(Guid patientId)
    {
        var observations = await _db.Observations
            .AsNoTracking()
            .Where(o => o.PatientId == patientId)
            .ToListAsync();

        return CreateBundle("searchset", observations.Select(o => MapObservationToFhir(o)));
    }

    public async Task<object> GetPatientAllergiesAsync(Guid patientId)
    {
        var allergies = await _db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == patientId && !a.Tombstone)
            .ToListAsync();

        return CreateBundle("searchset", allergies.Select(a => MapAllergyToFhir(a)));
    }

    public async Task<object> GetPatientConditionsAsync(Guid patientId)
    {
        var conditions = await _db.Conditions
            .AsNoTracking()
            .Where(c => c.PatientId == patientId)
            .ToListAsync();

        return CreateBundle("searchset", conditions.Select(c => MapConditionToFhir(c)));
    }

    public async Task<object> GetPatientMedicationsAsync(Guid patientId)
    {
        var prescriptions = await _db.Prescriptions
            .AsNoTracking()
            .Include(p => p.Medication)
            .Where(p => p.PatientId == patientId)
            .ToListAsync();

        return CreateBundle("searchset", prescriptions.Select(p => MapPrescriptionToFhir(p)));
    }

    public async Task<object> GetPatientAppointmentsAsync(Guid patientId)
    {
        var appointments = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .ToListAsync();

        return CreateBundle("searchset", appointments.Select(a => MapAppointmentToFhir(a)));
    }

    public async Task<object?> GetPatientEverythingBundleAsync(Guid patientId)
    {
        var patient = await _db.Patients
            .AsNoTracking()
            .Include(p => p.Person)
            .FirstOrDefaultAsync(p => p.Id == patientId);

        if (patient == null) return null;

        var entries = new List<object>
        {
            MapPatientToFhir(patient)
        };

        var observations = await _db.Observations.AsNoTracking().Where(o => o.PatientId == patientId).ToListAsync();
        entries.AddRange(observations.Select(MapObservationToFhir));

        var allergies = await _db.Allergies.AsNoTracking().Where(a => a.PatientId == patientId && !a.Tombstone).ToListAsync();
        entries.AddRange(allergies.Select(MapAllergyToFhir));

        var conditions = await _db.Conditions.AsNoTracking().Where(c => c.PatientId == patientId).ToListAsync();
        entries.AddRange(conditions.Select(MapConditionToFhir));

        var prescriptions = await _db.Prescriptions.AsNoTracking().Include(p => p.Medication).Where(p => p.PatientId == patientId).ToListAsync();
        entries.AddRange(prescriptions.Select(MapPrescriptionToFhir));

        var appointments = await _db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId).ToListAsync();
        entries.AddRange(appointments.Select(MapAppointmentToFhir));

        return CreateBundle("collection", entries);
    }

    private static object CreateBundle(string type, IEnumerable<object> resources)
    {
        var list = resources.ToList();
        return new
        {
            resourceType = "Bundle",
            type,
            total = list.Count,
            entry = list.Select(r => new { resource = r })
        };
    }

    private static object MapPatientToFhir(Patient p) => new
    {
        resourceType = "Patient",
        id = p.Id.ToString(),
        identifier = new[]
        {
            new { system = "urn:oid:medical-portal:mrn", value = p.Mrn }
        },
        name = new[]
        {
            new
            {
                use = "official",
                family = p.Person.LastName,
                given = new[] { p.Person.FirstName }
            }
        },
        gender = p.Person.SexAtBirth?.ToLowerInvariant() switch
        {
            "m" or "male" => "male",
            "f" or "female" => "female",
            _ => "unknown"
        },
        birthDate = p.Person.DateOfBirth.ToString("yyyy-MM-dd"),
        telecom = new[]
        {
            new { system = "phone", value = p.Person.Phone ?? "" },
            new { system = "email", value = p.Person.Email ?? "" }
        },
        address = new[]
        {
            new
            {
                line = new[] { p.Person.AddressLine1 ?? "" },
                city = p.Person.City ?? "",
                state = p.Person.State ?? "",
                postalCode = p.Person.PostalCode ?? ""
            }
        }
    };

    private static object MapObservationToFhir(Observation o) => new
    {
        resourceType = "Observation",
        id = o.Id.ToString(),
        status = "final",
        category = new[]
        {
            new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/observation-category", code = o.Category.ToString().ToLowerInvariant() } } }
        },
        code = new
        {
            coding = new[] { new { system = "http://loinc.org", code = o.Code, display = o.DisplayName } },
            text = o.DisplayName
        },
        subject = new { reference = $"Patient/{o.PatientId}" },
        effectiveDateTime = o.ObservedAt.ToString("o"),
        valueQuantity = o.ValueNumber.HasValue ? new { value = o.ValueNumber.Value, unit = o.Unit } : null,
        valueString = o.ValueText,
        interpretation = o.IsAbnormal ? new[] { new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation", code = "A", display = "Abnormal" } } } } : null
    };

    private static object MapAllergyToFhir(Allergy a) => new
    {
        resourceType = "AllergyIntolerance",
        id = a.Id.ToString(),
        clinicalStatus = new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical", code = "active" } } },
        verificationStatus = new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/allergyintolerance-verification", code = "confirmed" } } },
        criticality = a.Severity == Core.Enums.AllergySeverity.ANAPHYLAXIS ? "high" : "low",
        code = new
        {
            coding = new[] { new { system = "http://snomed.info/sct", code = a.DrugClassCode ?? "", display = a.Substance } },
            text = a.Substance
        },
        patient = new { reference = $"Patient/{a.PatientId}" },
        reaction = !string.IsNullOrEmpty(a.Reaction) ? new[] { new { manifestation = new[] { new { text = a.Reaction } } } } : null
    };

    private static object MapConditionToFhir(Condition c) => new
    {
        resourceType = "Condition",
        id = c.Id.ToString(),
        clinicalStatus = new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/condition-clinical", code = c.ClinicalStatus.ToLowerInvariant() } } },
        verificationStatus = new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/condition-ver-status", code = "confirmed" } } },
        code = new
        {
            coding = new[] { new { system = "http://hl7.org/fhir/sid/icd-10-cm", code = c.Code, display = c.Name } },
            text = c.Name
        },
        subject = new { reference = $"Patient/{c.PatientId}" },
        onsetDateTime = c.OnsetDate?.ToString("yyyy-MM-dd")
    };

    private static object MapPrescriptionToFhir(Prescription p) => new
    {
        resourceType = "MedicationRequest",
        id = p.Id.ToString(),
        status = p.IsActive ? "active" : "stopped",
        intent = "order",
        medicationCodeableConcept = new
        {
            coding = new[] { new { system = "http://www.nlm.nih.gov/research/umls/rxnorm", code = p.Medication.RxNormCode ?? "", display = p.Medication.Name } },
            text = p.Medication.Name
        },
        subject = new { reference = $"Patient/{p.PatientId}" },
        authoredOn = p.PrescribedAt.ToString("o"),
        dosageInstruction = new[]
        {
            new { text = $"{p.Dosage} {p.Frequency}. {p.Instructions}".Trim() }
        }
    };

    private static object MapAppointmentToFhir(Appointment a) => new
    {
        resourceType = "Appointment",
        id = a.Id.ToString(),
        status = a.Status switch
        {
            Core.Enums.AppointmentStatus.CONFIRMED => "booked",
            Core.Enums.AppointmentStatus.REQUESTED => "proposed",
            Core.Enums.AppointmentStatus.COMPLETED => "fulfilled",
            Core.Enums.AppointmentStatus.CANCELLED_BY_PATIENT or Core.Enums.AppointmentStatus.CANCELLED_BY_CLINIC => "cancelled",
            _ => "noshow"
        },
        description = a.ReasonForVisit,
        start = a.StartAt.ToString("o"),
        end = a.EndAt.ToString("o"),
        participant = new[]
        {
            new { actor = new { reference = $"Patient/{a.PatientId}" }, status = "accepted" },
            new { actor = new { reference = $"Practitioner/{a.ProviderId}" }, status = "accepted" }
        }
    };
}
