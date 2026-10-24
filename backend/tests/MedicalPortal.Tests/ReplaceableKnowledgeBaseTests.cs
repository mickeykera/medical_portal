using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class ReplaceableKnowledgeBaseTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReplaceableKnowledgeBaseTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (UserAccount account, string token, Guid personId, Provider provider) CreateClinician(MedicalPortalDbContext db, ITokenService tokens, IPasswordHasher hasher)
    {
        var email = $"dr.{Guid.NewGuid():N}@clinic.example";
        var person = new Person
        {
            FirstName = "Doctor",
            LastName = "Specialist",
            Email = email,
            DateOfBirth = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Persons.Add(person);

        var account = new UserAccount
        {
            Person = person,
            Email = email,
            PasswordHash = hasher.HashPassword("Clinician!Pass123"),
            Role = UserRole.CLINICIAN,
            IsActive = true
        };
        db.UserAccounts.Add(account);

        var provider = new Provider
        {
            AccountId = account.Id,
            FirstName = person.FirstName,
            LastName = person.LastName,
            Specialty = "Internal Medicine"
        };
        db.Providers.Add(provider);

        db.SaveChanges();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);
        return (account, token, person.Id, provider);
    }

    private Patient CreateTestPatient(MedicalPortalDbContext db, Provider provider, string? allergySubstance = null, string? drugClass = null, string? drugClassCode = null)
    {
        var person = new Person
        {
            FirstName = "Patient",
            LastName = $"Test-{Guid.NewGuid():N}"[..8],
            DateOfBirth = new DateTime(1990, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Email = $"pat.{Guid.NewGuid():N}@example.com"
        };
        db.Persons.Add(person);

        var patient = new Patient
        {
            Person = person,
            Mrn = $"MRN-{Guid.NewGuid():N}"[..10].ToUpperInvariant()
        };
        db.Patients.Add(patient);

        db.CareTeams.Add(new PatientCareTeam
        {
            Patient = patient,
            Provider = provider,
            IsPrimary = true,
            Role = "Primary Physician"
        });

        if (!string.IsNullOrEmpty(allergySubstance))
        {
            var allergy = new Allergy
            {
                Patient = patient,
                Substance = allergySubstance,
                DrugClass = drugClass ?? allergySubstance,
                DrugClassCode = drugClassCode ?? "J01CA",
                Severity = AllergySeverity.ANAPHYLAXIS,
                Reaction = "Anaphylactic shock"
            };
            db.Allergies.Add(allergy);
        }

        db.SaveChanges();
        return patient;
    }

    [Fact]
    public void DemoInteractionKnowledgeBase_LabeledAsDemo_DoesNotClaimCompleteness()
    {
        var kb = new DemoInteractionKnowledgeBase();

        Assert.Contains("demo", kb.Version, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEMO ONLY", kb.ProviderName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEMO DATA ONLY", kb.Disclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does NOT claim clinical completeness", kb.Disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DemoAllergyKnowledgeBase_LabeledAsDemo_DoesNotClaimCompleteness()
    {
        var kb = new DemoAllergyKnowledgeBase();

        Assert.Contains("demo", kb.Version, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEMO ONLY", kb.ProviderName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEMO DATA ONLY", kb.Disclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does NOT claim clinical completeness", kb.Disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DemoInteractionKnowledgeBase_MatchesUsingRxNormCodes()
    {
        var kb = new DemoInteractionKnowledgeBase();

        // Warfarin (RxNorm 855332) + Ibuprofen (RxNorm 5640)
        var existing = new[] { new MedicationConcept("Unspecified Anticoagulant", "855332") };
        var proposed = new MedicationConcept("Unspecified NSAID", "5640");

        var warnings = kb.CheckInteractions(existing, proposed);
        Assert.NotEmpty(warnings);
        Assert.Equal(InteractionSeverity.HIGH, warnings[0].Severity);
        Assert.Contains("bleeding", warnings[0].Description, StringComparison.OrdinalIgnoreCase);

        // Lisinopril (RxNorm 314076) + Spironolactone (RxNorm 9997)
        var acei = new[] { new MedicationConcept("ACEI Product", "314076") };
        var potassiumSparing = new MedicationConcept("Diuretic Product", "9997");
        var aceiWarnings = kb.CheckInteractions(acei, potassiumSparing);
        Assert.NotEmpty(aceiWarnings);
        Assert.Equal(InteractionSeverity.HIGH, aceiWarnings[0].Severity);
        Assert.Contains("hyperkalemia", aceiWarnings[0].Description, StringComparison.OrdinalIgnoreCase);

        // Sertraline (RxNorm 36437) + Tramadol (RxNorm 10689)
        var ssri = new[] { new MedicationConcept("Antidepressant", "36437") };
        var analgesic = new MedicationConcept("Analgesic", "10689");
        var ssriWarnings = kb.CheckInteractions(ssri, analgesic);
        Assert.NotEmpty(ssriWarnings);
        Assert.Equal(InteractionSeverity.HIGH, ssriWarnings[0].Severity);
        Assert.Contains("Serotonin Syndrome", ssriWarnings[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DemoAllergyKnowledgeBase_MatchesUsingRxNormCodes()
    {
        var kb = new DemoAllergyKnowledgeBase();

        var allergies = new List<Allergy>
        {
            new()
            {
                Substance = "Penicillin",
                DrugClass = "Beta-lactam (penicillin)",
                DrugClassCode = "J01CA",
                Severity = AllergySeverity.ANAPHYLAXIS
            }
        };

        // Amoxicillin via RxNorm code 723 (even if Name is arbitrary)
        var proposedMed = new MedicationConcept("Broad-spectrum Capsule", "723");
        var result = kb.CheckPrescription(proposedMed, allergies);

        Assert.False(result.Safe);
        Assert.Contains("cross-reactivity", result.BlockReason, StringComparison.OrdinalIgnoreCase);

        // Unrelated medication via RxNorm code 860975 (Metformin)
        var safeMed = new MedicationConcept("Glucophage Generic", "860975");
        var safeResult = kb.CheckPrescription(safeMed, allergies);
        Assert.True(safeResult.Safe);
    }

    [Fact]
    public void LicensedDrugDatabase_CanBePluggedInUsingRxNorm()
    {
        // 1. Create a licensed knowledge base adapter with licensed vendor metadata
        var licensedKb = new LicensedRxNormKnowledgeBase(
            version: "fdb-nddf-2024.10",
            providerName: "First Databank NDDF Licensed Feed",
            disclaimer: "Licensed commercial clinical pharmacology data feed. Decision support only; does not claim clinical completeness.");

        // 2. Register licensed RxNorm interaction rule (e.g. Clopidogrel 32968 + Omeprazole 7646)
        licensedKb.RegisterInteractionRule(new RxNormInteractionRule(
            RuleId: "FDB-DDI-00123",
            RxNormCodesA: new[] { "32968" }, // Clopidogrel
            RxNormCodesB: new[] { "7646" },  // Omeprazole
            Severity: InteractionSeverity.MODERATE,
            Description: "Omeprazole inhibits CYP2C19 bioactivation of clopidogrel, significantly reducing antiplatelet efficacy.",
            ClinicalGuidance: "Consider alternative gastroprotection such as pantoprazole or famotidine."
        ));

        // 3. Register licensed allergy cross-reactivity rule
        licensedKb.RegisterAllergyRule(new RxNormAllergyRule(
            RuleId: "FDB-ALLERGY-00456",
            AllergenClassOrCode: "J01DB", // 1st gen cephalosporins
            ContraindicatedRxNormCodes: new[] { "2231" }, // Cephalexin
            Severity: AllergySeverity.SEVERE,
            Reason: "Severe beta-lactam side chain cross-reactivity with 1st generation cephalosporins."
        ));

        // Test interaction detection via RxNorm
        var existing = new[] { new MedicationConcept("Plavix", "32968") };
        var proposed = new MedicationConcept("Prilosec", "7646");
        var ddi = licensedKb.CheckInteractions(existing, proposed);

        Assert.NotEmpty(ddi);
        Assert.Equal(InteractionSeverity.MODERATE, ddi[0].Severity);
        Assert.Contains("CYP2C19", ddi[0].Description);

        // Test allergy cross-reactivity via RxNorm
        var allergies = new List<Allergy>
        {
            new()
            {
                Substance = "Cefazolin",
                DrugClass = "First generation cephalosporins",
                DrugClassCode = "J01DB",
                Severity = AllergySeverity.SEVERE
            }
        };

        var proposedRx = new MedicationConcept("Keflex", "2231");
        var allergyCheck = licensedKb.CheckPrescription(proposedRx, allergies);
        Assert.False(allergyCheck.Safe);
        Assert.Contains("Cross-reactivity warning", allergyCheck.BlockReason);
    }

    [Fact]
    public void StaticLogics_AllowReplacingDefaultKnowledgeBase()
    {
        var originalDdiKb = DrugInteractionLogic.DefaultKnowledgeBase;
        var originalAllergyKb = AllergyLogic.DefaultKnowledgeBase;

        try
        {
            var customKb = new LicensedRxNormKnowledgeBase(
                version: "custom-test-kb-v1",
                providerName: "Test Provider");

            customKb.RegisterInteractionRule(new RxNormInteractionRule(
                RuleId: "CUSTOM-01",
                RxNormCodesA: new[] { "99901" },
                RxNormCodesB: new[] { "99902" },
                Severity: InteractionSeverity.HIGH,
                Description: "Custom test interaction",
                ClinicalGuidance: "Test guidance"
            ));

            DrugInteractionLogic.DefaultKnowledgeBase = customKb;

            var existing = new[] { new MedicationConcept("Med A", "99901") };
            var proposed = new MedicationConcept("Med B", "99902");

            var warnings = DrugInteractionLogic.CheckDrugInteractions(existing, proposed);
            Assert.NotEmpty(warnings);
            Assert.Equal("Custom test interaction", warnings[0].Description);
        }
        finally
        {
            DrugInteractionLogic.DefaultKnowledgeBase = originalDdiKb;
            AllergyLogic.DefaultKnowledgeBase = originalAllergyKb;
        }
    }

    [Fact]
    public async Task Api_SafetyCheck_RecordsKnowledgeBaseVersionInAuditLog()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, clinicianToken, _, provider) = CreateClinician(db, tokens, hasher);
        var patient = CreateTestPatient(db, provider, "Penicillin", "Beta-lactam (penicillin)", "J01CA");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clinicianToken);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());

        var res = await client.PostAsJsonAsync("/api/clinical/prescriptions/safety-check", new
        {
            medicationName = "Amoxicillin",
            rxNormCode = "723"
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("safe").GetBoolean());
        Assert.True(body.GetProperty("hasAllergyConflict").GetBoolean());

        // Verify knowledge base metadata in response
        var kbElement = body.GetProperty("knowledgeBases");
        Assert.Equal(DemoInteractionKnowledgeBase.DemoVersion, kbElement.GetProperty("interactionVersion").GetString());
        Assert.Equal(DemoAllergyKnowledgeBase.DemoVersion, kbElement.GetProperty("allergyVersion").GetString());
        Assert.Contains("does not claim clinical completeness", kbElement.GetProperty("disclaimer").GetString(), StringComparison.OrdinalIgnoreCase);

        // Verify compliance audit trail was written
        var audit = await db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "CLINICAL_SAFETY_CHECK" && a.PatientId == patient.Id);

        Assert.NotNull(audit);
        Assert.Contains(DemoInteractionKnowledgeBase.DemoVersion, audit.MetadataJson);
        Assert.Contains(DemoAllergyKnowledgeBase.DemoVersion, audit.MetadataJson);
        Assert.Contains("does not claim clinical completeness", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Api_PrescribeWithOverride_RecordsKnowledgeBaseVersionInAuditLog()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, clinicianToken, _, provider) = CreateClinician(db, tokens, hasher);
        var patient = CreateTestPatient(db, provider, "Penicillin", "Beta-lactam (penicillin)", "J01CA");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clinicianToken);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());

        // 1. Prescribing without override justification should fail with 409 Conflict
        var blockedRes = await client.PostAsJsonAsync("/api/clinical/prescriptions", new
        {
            medicationName = "Amoxicillin",
            rxNormCode = "723",
            dosage = "500 mg",
            frequency = "TID",
            instructions = "Take with meals"
        });
        Assert.Equal(HttpStatusCode.Conflict, blockedRes.StatusCode);

        // 2. Prescribing with override justification succeeds and records KB versions
        var successRes = await client.PostAsJsonAsync("/api/clinical/prescriptions", new
        {
            medicationName = "Amoxicillin",
            rxNormCode = "723",
            dosage = "500 mg",
            frequency = "TID",
            instructions = "Take with meals",
            overrideJustification = "Desensitization protocol completed under ICU supervision"
        });
        Assert.Equal(HttpStatusCode.Created, successRes.StatusCode);

        // Check override audit log
        var overrideAudit = await db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "SAFETY_CONTRAINDICATION_OVERRIDDEN" && a.PatientId == patient.Id);

        Assert.NotNull(overrideAudit);
        Assert.Contains(DemoInteractionKnowledgeBase.DemoVersion, overrideAudit.MetadataJson);
        Assert.Contains(DemoAllergyKnowledgeBase.DemoVersion, overrideAudit.MetadataJson);
        Assert.Contains("Desensitization protocol", overrideAudit.MetadataJson);

        // Check prescription issued audit log
        var issuedAudit = await db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "PRESCRIPTION_ISSUED" && a.PatientId == patient.Id);

        Assert.NotNull(issuedAudit);
        Assert.Contains(DemoInteractionKnowledgeBase.DemoVersion, issuedAudit.MetadataJson);
        Assert.Contains(DemoAllergyKnowledgeBase.DemoVersion, issuedAudit.MetadataJson);
    }

    [Fact]
    public async Task Api_PluggingLicensedKnowledgeBase_ReflectedInAuditLog()
    {
        const string customVersion = "fdb-nddf-2024.11-custom";
        var licensedKb = new LicensedRxNormKnowledgeBase(
            version: customVersion,
            providerName: "First Databank NDDF");

        licensedKb.RegisterInteractionRule(new RxNormInteractionRule(
            RuleId: "FDB-999",
            RxNormCodesA: new[] { "314076" }, // Lisinopril
            RxNormCodesB: new[] { "9997" },   // Spironolactone
            Severity: InteractionSeverity.HIGH,
            Description: "Dual potassium retention hazard from licensed provider.",
            ClinicalGuidance: "Monitor potassium."
        ));

        // Build factory with custom licensed knowledge base injected
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IInteractionKnowledgeBase>(licensedKb);
            });
        });

        using var scope = customFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (_, clinicianToken, _, provider) = CreateClinician(db, tokens, hasher);
        var patient = CreateTestPatient(db, provider);

        // Add active prescription for Lisinopril
        var medLisinopril = new Medication { Name = "Lisinopril", RxNormCode = "314076" };
        db.Medications.Add(medLisinopril);
        db.Prescriptions.Add(new Prescription
        {
            Patient = patient,
            Medication = medLisinopril,
            Provider = provider,
            Dosage = "10mg",
            Frequency = "Daily",
            IsActive = true
        });
        db.SaveChanges();

        var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clinicianToken);
        client.DefaultRequestHeaders.Add("x-patient-id", patient.Id.ToString());

        var res = await client.PostAsJsonAsync("/api/clinical/prescriptions/safety-check", new
        {
            medicationName = "Spironolactone",
            rxNormCode = "9997"
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("hasDrugInteraction").GetBoolean());
        Assert.Equal(customVersion, body.GetProperty("knowledgeBases").GetProperty("interactionVersion").GetString());

        // Verify audit log has recorded the licensed version
        var audit = await db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "CLINICAL_SAFETY_CHECK" && a.PatientId == patient.Id);

        Assert.NotNull(audit);
        Assert.Contains(customVersion, audit.MetadataJson);
        Assert.Contains("First Databank NDDF", audit.MetadataJson);
    }
}
