namespace MedicalPortal.Infrastructure.Fhir;

public class FakeClinicalCoreFhirClient : IClinicalCoreFhirClient
{
    public List<FhirPatientDto> Patients { get; } = new();
    public List<FhirEncounterDto> Encounters { get; } = new();
    public List<FhirObservationDto> Observations { get; } = new();
    public List<FhirAllergyIntoleranceDto> Allergies { get; } = new();
    public List<FhirMedicationRequestDto> Medications { get; } = new();
    public List<FhirAppointmentDto> Appointments { get; } = new();

    public bool SimulateNetworkFailure { get; set; } = false;

    public FakeClinicalCoreFhirClient()
    {
        SeedSyntheticData();
    }

    private void SeedSyntheticData()
    {
        // 1. Synthetic Patient (Abebe Kebede)
        var p1 = new FhirPatientDto(
            ResourceType: "Patient",
            Id: "patient-1001",
            Active: true,
            Identifier: new List<FhirIdentifierDto>
            {
                new("http://openmrs.org/ext/mrn", "MRN-1001", "usual"),
                new("urn:oid:medical-portal:national-id", "ET-NID-998877", "official")
            },
            Name: new List<FhirHumanNameDto>
            {
                new("official", "Kebede", new List<string> { "Abebe", "Tadesse" })
            },
            Telecom: new List<FhirTelecomDto>
            {
                new("phone", "+251911234567", "mobile"),
                new("email", "abebe.kebede.synthetic@hospital.internal", "home")
            },
            Gender: "male",
            BirthDate: "1985-04-12",
            Address: new List<FhirAddressDto>
            {
                new(new List<string> { "Bole Sub-City, Woreda 03" }, "Addis Ababa", "Addis Ababa", "1000", "Ethiopia")
            }
        );
        Patients.Add(p1);

        // 2. Synthetic Encounter
        var enc1 = new FhirEncounterDto(
            ResourceType: "Encounter",
            Id: "enc-5001",
            Status: "finished",
            Class: new FhirCodingDto("http://terminology.hl7.org/CodeSystem/v3-ActCode", "AMB", "ambulatory"),
            Type: new List<FhirCodeableConceptDto>
            {
                new(new List<FhirCodingDto> { new("http://openmrs.org", "OPD_INTERNAL_MED", "Internal Medicine OPD") }, "Outpatient Consultation")
            },
            Subject: new FhirReferenceDto("Patient/patient-1001", "Abebe Kebede"),
            Participant: new List<FhirEncounterParticipantDto>
            {
                new(new FhirReferenceDto("Practitioner/doc-201", "Dr. Dawit Alemu"), null)
            },
            Period: new FhirPeriodDto("2026-10-20T08:30:00Z", "2026-10-20T09:15:00Z"),
            ReasonCode: new List<FhirCodeableConceptDto>
            {
                new(new List<FhirCodingDto> { new("http://snomed.info/sct", "38341003", "Hypertensive disorder") }, "Essential Hypertension")
            }
        );
        Encounters.Add(enc1);

        // 3. Synthetic Observations (Blood Pressure & Glucose)
        var obsBp = new FhirObservationDto(
            ResourceType: "Observation",
            Id: "obs-7001",
            Status: "final",
            Category: new List<FhirCodeableConceptDto>
            {
                new(new List<FhirCodingDto> { new("http://terminology.hl7.org/CodeSystem/observation-category", "vital-signs", "Vital Signs") }, null)
            },
            Code: new FhirCodeableConceptDto(
                new List<FhirCodingDto> { new("http://loinc.org", "85354-9", "Blood pressure panel") },
                "Blood Pressure"
            ),
            Subject: new FhirReferenceDto("Patient/patient-1001", "Abebe Kebede"),
            EffectiveDateTime: "2026-10-20T08:45:00Z",
            ValueQuantity: null,
            ValueString: null,
            Component: new List<FhirObservationComponentDto>
            {
                new(
                    new FhirCodeableConceptDto(new List<FhirCodingDto> { new("http://loinc.org", "8480-6", "Systolic blood pressure") }, "Systolic"),
                    new FhirQuantityDto(130m, "mmHg", "http://unitsofmeasure.org", "mm[Hg]"),
                    null
                ),
                new(
                    new FhirCodeableConceptDto(new List<FhirCodingDto> { new("http://loinc.org", "8462-4", "Diastolic blood pressure") }, "Diastolic"),
                    new FhirQuantityDto(85m, "mmHg", "http://unitsofmeasure.org", "mm[Hg]"),
                    null
                )
            }
        );
        Observations.Add(obsBp);

        // 4. Synthetic Allergy (Penicillin)
        var allergy1 = new FhirAllergyIntoleranceDto(
            ResourceType: "AllergyIntolerance",
            Id: "allergy-3001",
            ClinicalStatus: new FhirCodeableConceptDto(
                new List<FhirCodingDto> { new("http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical", "active", "Active") }, null),
            VerificationStatus: new FhirCodeableConceptDto(
                new List<FhirCodingDto> { new("http://terminology.hl7.org/CodeSystem/allergyintolerance-verification", "confirmed", "Confirmed") }, null),
            Type: "allergy",
            Category: new List<string> { "medication" },
            Criticality: "high",
            Code: new FhirCodeableConceptDto(
                new List<FhirCodingDto> { new("http://snomed.info/sct", "373270004", "Substance with penicillin structure") },
                "Penicillin G"
            ),
            Patient: new FhirReferenceDto("Patient/patient-1001", "Abebe Kebede"),
            RecordedDate: "2025-06-15T11:00:00Z",
            Reaction: new List<FhirAllergyReactionDto>
            {
                new(
                    new FhirCodeableConceptDto(new List<FhirCodingDto> { new("http://snomed.info/sct", "373270004", "Penicillin") }, "Penicillin"),
                    new List<FhirCodeableConceptDto> { new(null, "Anaphylaxis and generalized urticaria") },
                    "severe"
                )
            }
        );
        Allergies.Add(allergy1);

        // 5. Synthetic MedicationRequest (Amlodipine)
        var med1 = new FhirMedicationRequestDto(
            ResourceType: "MedicationRequest",
            Id: "med-8001",
            Status: "active",
            Intent: "order",
            MedicationCodeableConcept: new FhirCodeableConceptDto(
                new List<FhirCodingDto> { new("http://www.nlm.nih.gov/research/umls/rxnorm", "17767", "Amlodipine 5 MG Oral Tablet") },
                "Amlodipine 5mg Daily"
            ),
            Subject: new FhirReferenceDto("Patient/patient-1001", "Abebe Kebede"),
            AuthoredOn: "2026-10-20T09:10:00Z",
            Requester: new FhirReferenceDto("Practitioner/doc-201", "Dr. Dawit Alemu"),
            DosageInstruction: new List<FhirDosageInstructionDto>
            {
                new("Take 1 tablet (5mg) by mouth once daily in the morning", null, false)
            }
        );
        Medications.Add(med1);

        // 6. Synthetic Appointment
        var appt1 = new FhirAppointmentDto(
            ResourceType: "Appointment",
            Id: "appt-9001",
            Status: "booked",
            ServiceCategory: new List<FhirCodeableConceptDto>
            {
                new(new List<FhirCodingDto> { new("http://openmrs.org", "INTERNAL_MED", "Internal Medicine") }, "Internal Medicine")
            },
            Description: "Routine Follow-up Consultation",
            Start: "2026-11-10T09:00:00Z",
            End: "2026-11-10T09:30:00Z",
            Participant: new List<FhirAppointmentParticipantDto>
            {
                new(new FhirReferenceDto("Patient/patient-1001", "Abebe Kebede"), "accepted", "required"),
                new(new FhirReferenceDto("Practitioner/doc-201", "Dr. Dawit Alemu"), "accepted", "required")
            },
            Comment: "Check BP control after amlodipine adjustment"
        );
        Appointments.Add(appt1);
    }

    public Task<FhirCapabilityStatementDto?> GetCapabilityStatementAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure) return Task.FromResult<FhirCapabilityStatementDto?>(null);

        var capability = new FhirCapabilityStatementDto(
            ResourceType: "CapabilityStatement",
            Status: "active",
            Date: "2026-10-24T00:00:00Z",
            FhirVersion: "4.0.1",
            Software: new FhirSoftwareDto("OpenMRS FHIR2 Platform", "2.8.0")
        );
        return Task.FromResult<FhirCapabilityStatementDto?>(capability);
    }

    public Task<FhirPatientDto?> GetPatientByIdAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure) return Task.FromResult<FhirPatientDto?>(null);

        var patient = Patients.FirstOrDefault(p => p.Id == fhirPatientId);
        return Task.FromResult(patient);
    }

    public Task<FhirPatientDto?> GetPatientByMrnAsync(string mrn, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure) return Task.FromResult<FhirPatientDto?>(null);

        var patient = Patients.FirstOrDefault(p => p.Mrn?.Equals(mrn, StringComparison.OrdinalIgnoreCase) == true);
        return Task.FromResult(patient);
    }

    public Task<FhirBundleDto<FhirEncounterDto>> GetEncountersAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure)
            return Task.FromResult(new FhirBundleDto<FhirEncounterDto>("Bundle", "searchset", 0, new List<FhirBundleEntryDto<FhirEncounterDto>>()));

        var refStr = $"Patient/{fhirPatientId}";
        var matches = Encounters.Where(e => e.Subject?.Reference == refStr).ToList();
        var entries = matches.Select(m => new FhirBundleEntryDto<FhirEncounterDto>($"http://openmrs/Encounter/{m.Id}", m)).ToList();

        return Task.FromResult(new FhirBundleDto<FhirEncounterDto>("Bundle", "searchset", matches.Count, entries));
    }

    public Task<FhirBundleDto<FhirObservationDto>> GetObservationsAsync(string fhirPatientId, string? category = null, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure)
            return Task.FromResult(new FhirBundleDto<FhirObservationDto>("Bundle", "searchset", 0, new List<FhirBundleEntryDto<FhirObservationDto>>()));

        var refStr = $"Patient/{fhirPatientId}";
        var matches = Observations.Where(o => o.Subject?.Reference == refStr).ToList();

        if (!string.IsNullOrEmpty(category))
        {
            matches = matches.Where(o => o.Category?.Any(c => c.Coding?.Any(cod => cod.Code == category) == true) == true).ToList();
        }

        var entries = matches.Select(m => new FhirBundleEntryDto<FhirObservationDto>($"http://openmrs/Observation/{m.Id}", m)).ToList();
        return Task.FromResult(new FhirBundleDto<FhirObservationDto>("Bundle", "searchset", matches.Count, entries));
    }

    public Task<FhirBundleDto<FhirAllergyIntoleranceDto>> GetAllergiesAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure)
            return Task.FromResult(new FhirBundleDto<FhirAllergyIntoleranceDto>("Bundle", "searchset", 0, new List<FhirBundleEntryDto<FhirAllergyIntoleranceDto>>()));

        var refStr = $"Patient/{fhirPatientId}";
        var matches = Allergies.Where(a => a.Patient?.Reference == refStr).ToList();
        var entries = matches.Select(m => new FhirBundleEntryDto<FhirAllergyIntoleranceDto>($"http://openmrs/AllergyIntolerance/{m.Id}", m)).ToList();

        return Task.FromResult(new FhirBundleDto<FhirAllergyIntoleranceDto>("Bundle", "searchset", matches.Count, entries));
    }

    public Task<FhirBundleDto<FhirMedicationRequestDto>> GetMedicationsAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure)
            return Task.FromResult(new FhirBundleDto<FhirMedicationRequestDto>("Bundle", "searchset", 0, new List<FhirBundleEntryDto<FhirMedicationRequestDto>>()));

        var refStr = $"Patient/{fhirPatientId}";
        var matches = Medications.Where(m => m.Subject?.Reference == refStr).ToList();
        var entries = matches.Select(m => new FhirBundleEntryDto<FhirMedicationRequestDto>($"http://openmrs/MedicationRequest/{m.Id}", m)).ToList();

        return Task.FromResult(new FhirBundleDto<FhirMedicationRequestDto>("Bundle", "searchset", matches.Count, entries));
    }

    public Task<FhirBundleDto<FhirAppointmentDto>> GetAppointmentsAsync(string fhirPatientId, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure)
            return Task.FromResult(new FhirBundleDto<FhirAppointmentDto>("Bundle", "searchset", 0, new List<FhirBundleEntryDto<FhirAppointmentDto>>()));

        var refStr = $"Patient/{fhirPatientId}";
        var matches = Appointments.Where(a => a.Participant?.Any(p => p.Actor?.Reference == refStr) == true).ToList();
        var entries = matches.Select(m => new FhirBundleEntryDto<FhirAppointmentDto>($"http://openmrs/Appointment/{m.Id}", m)).ToList();

        return Task.FromResult(new FhirBundleDto<FhirAppointmentDto>("Bundle", "searchset", matches.Count, entries));
    }

    public Task<FhirAppointmentDto?> CreateAppointmentAsync(FhirAppointmentDto appointment, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkFailure) return Task.FromResult<FhirAppointmentDto?>(null);

        var assignedId = appointment.Id ?? $"appt-{Guid.NewGuid():N}"[..12];
        var created = appointment with { Id = assignedId };
        Appointments.Add(created);
        return Task.FromResult<FhirAppointmentDto?>(created);
    }

    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!SimulateNetworkFailure);
    }
}
