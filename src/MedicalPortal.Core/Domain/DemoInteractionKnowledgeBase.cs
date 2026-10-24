namespace MedicalPortal.Core.Domain;

/// <summary>
/// DEMONSTRATION / REFERENCE DATA ONLY.
/// This interaction knowledge base contains a limited sample dataset intended solely
/// for automated testing, development, and system demonstration.
/// 
/// IMPORTANT DISCLAIMER:
/// This implementation is NOT clinically complete or exhaustive and MUST NOT be used
/// for real-world clinical decision-making or patient care without integration of a
/// certified, licensed drug knowledge provider (e.g., First Databank, Wolters Kluwer Medi-Span).
/// </summary>
public class DemoInteractionKnowledgeBase : IInteractionKnowledgeBase
{
    public const string DemoVersion = "demo-interactions-2024.1-sample";

    public string Version => DemoVersion;
    public string ProviderName => "Springfield Reference Knowledge Base (DEMO ONLY)";
    public string Disclaimer => "DEMO DATA ONLY: This dataset contains limited sample interaction rules for demonstration and automated testing. It does NOT claim clinical completeness or exhaustiveness. Always use an accredited clinical pharmacology database for production.";

    public record DemoInteractionRule(
        string[] ClassAKeywords,
        string[] ClassARxNorms,
        string[] ClassBKeywords,
        string[] ClassBRxNorms,
        InteractionSeverity Severity,
        string Description,
        string ClinicalGuidance);

    private static readonly List<DemoInteractionRule> Rules = new()
    {
        // 1. Anticoagulants + NSAIDs
        new DemoInteractionRule(
            ClassAKeywords: new[] { "warfarin", "apixaban", "rivaroxaban", "dabigatran", "heparin", "enoxaparin", "clopidogrel" },
            ClassARxNorms: new[] { "855332", "11289", "1364430", "1114195", "1037046", "5224", "67108", "32968" },
            ClassBKeywords: new[] { "ibuprofen", "naproxen", "ketorolac", "meloxicam", "celecoxib", "aspirin", "diclofenac", "indomethacin" },
            ClassBRxNorms: new[] { "5640", "7258", "6142", "214199", "140587", "1191", "3355", "5807" },
            Severity: InteractionSeverity.HIGH,
            Description: "Co-administration of anticoagulants/antiplatelets and NSAIDs significantly increases the risk of major gastrointestinal and systemic bleeding.",
            ClinicalGuidance: "Avoid concurrent use where possible. If anti-inflammatory analgesia is required, consider acetaminophen or topical options with gastroprotection."
        ),

        // 2. ACE Inhibitors / ARBs + Potassium Supplements or Potassium-sparing Diuretics
        new DemoInteractionRule(
            ClassAKeywords: new[] { "lisinopril", "enalapril", "ramipril", "losartan", "valsartan", "candesartan" },
            ClassARxNorms: new[] { "314076", "29046", "3827", "35296", "52175", "69749", "214354" },
            ClassBKeywords: new[] { "spironolactone", "eplerenone", "triamterene", "potassium chloride", "potassium citrate" },
            ClassBRxNorms: new[] { "9997", "295657", "10767", "8591", "8597" },
            Severity: InteractionSeverity.HIGH,
            Description: "Dual potassium retention mechanism creates a severe risk of life-threatening hyperkalemia and cardiac arrhythmias.",
            ClinicalGuidance: "Monitor serum potassium and renal function within 1-2 weeks of initiation. Advise patient against high-potassium salt substitutes."
        ),

        // 3. SSRIs / SNRIs + Tramadol / MAOIs / Triptans (Serotonin Syndrome)
        new DemoInteractionRule(
            ClassAKeywords: new[] { "sertraline", "fluoxetine", "escitalopram", "citalopram", "paroxetine", "venlafaxine", "duloxetine" },
            ClassARxNorms: new[] { "36437", "4493", "32937", "2556", "39786", "72625" },
            ClassBKeywords: new[] { "tramadol", "linezolid", "phenelzine", "selegiline", "sumatriptan" },
            ClassBRxNorms: new[] { "10689", "190376", "8123", "9677", "10244" },
            Severity: InteractionSeverity.HIGH,
            Description: "Concurrent serotonergic agents can precipitate Serotonin Syndrome (hyperthermia, autonomic instability, neuromuscular hyperreactivity).",
            ClinicalGuidance: "Avoid combination. If tramadol must be used, titrate with strict vigilance for neuromuscular symptoms and confusion."
        ),

        // 4. Metformin + Contrast Media
        new DemoInteractionRule(
            ClassAKeywords: new[] { "metformin" },
            ClassARxNorms: new[] { "860975", "6809" },
            ClassBKeywords: new[] { "iodinated contrast", "iopamidol", "iohexol", "contrast agent" },
            ClassBRxNorms: new[] { "58477" },
            Severity: InteractionSeverity.HIGH,
            Description: "Radiographic iodinated contrast may cause acute impairment of renal function, precipitating fatal metformin-induced lactic acidosis.",
            ClinicalGuidance: "Discontinue metformin prior to or at time of procedure and withhold for 48 hours until renal function is confirmed normal."
        ),

        // 5. Statins + Strong CYP3A4 Inhibitors
        new DemoInteractionRule(
            ClassAKeywords: new[] { "simvastatin", "atorvastatin", "lovastatin" },
            ClassARxNorms: new[] { "36567", "83367", "6472" },
            ClassBKeywords: new[] { "clarithromycin", "erythromycin", "ketoconazole", "itraconazole", "diltiazem" },
            ClassBRxNorms: new[] { "21106", "4053", "6135", "28031", "3443" },
            Severity: InteractionSeverity.MODERATE,
            Description: "CYP3A4 inhibition drastically increases statin serum concentrations, increasing the risk of myopathy and rhabdomyolysis.",
            ClinicalGuidance: "Temporarily withhold the statin during the antibiotic/antifungal course or switch to a non-CYP3A4 statin (e.g. pravastatin, rosuvastatin)."
        ),

        // 6. Fluoroquinolones + Corticosteroids
        new DemoInteractionRule(
            ClassAKeywords: new[] { "ciprofloxacin", "levofloxacin", "moxifloxacin" },
            ClassARxNorms: new[] { "2551", "82122", "139462" },
            ClassBKeywords: new[] { "prednisone", "prednisolone", "dexamethasone", "methylprednisolone" },
            ClassBRxNorms: new[] { "8640", "8638", "3264", "6902" },
            Severity: InteractionSeverity.MODERATE,
            Description: "Co-administration increases the risk of severe tendonitis and Achilles tendon rupture, especially in older adults.",
            ClinicalGuidance: "Select an alternative antimicrobial agent if feasible. Warn patient to discontinue exercise and report any tendon pain immediately."
        ),

        // 7. Macrolides + QT Prolonging Agents
        new DemoInteractionRule(
            ClassAKeywords: new[] { "azithromycin", "clarithromycin", "erythromycin" },
            ClassARxNorms: new[] { "18631", "21106", "4053" },
            ClassBKeywords: new[] { "amiodarone", "sotalol", "haloperidol", "quetiapine", "ondansetron" },
            ClassBRxNorms: new[] { "703", "9959", "5032", "77063", "26225" },
            Severity: InteractionSeverity.MODERATE,
            Description: "Additive prolongation of the QTc interval elevates the risk of polymorphic ventricular tachycardia (Torsades de Pointes).",
            ClinicalGuidance: "Obtain baseline ECG and monitor electrolytes. Prefer non-QT prolonging alternatives like amoxicillin or doxycycline if indicated."
        )
    };

    private static readonly Dictionary<string, string> NameToRxNormMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["warfarin"] = "855332",
        ["apixaban"] = "1364430",
        ["rivaroxaban"] = "1114195",
        ["dabigatran"] = "1037046",
        ["heparin"] = "5224",
        ["enoxaparin"] = "67108",
        ["clopidogrel"] = "32968",
        ["ibuprofen"] = "5640",
        ["naproxen"] = "7258",
        ["ketorolac"] = "6142",
        ["meloxicam"] = "214199",
        ["celecoxib"] = "140587",
        ["aspirin"] = "1191",
        ["diclofenac"] = "3355",
        ["indomethacin"] = "5807",
        ["lisinopril"] = "314076",
        ["enalapril"] = "3827",
        ["ramipril"] = "35296",
        ["losartan"] = "52175",
        ["valsartan"] = "69749",
        ["candesartan"] = "214354",
        ["spironolactone"] = "9997",
        ["eplerenone"] = "295657",
        ["triamterene"] = "10767",
        ["potassium chloride"] = "8591",
        ["potassium citrate"] = "8597",
        ["sertraline"] = "36437",
        ["fluoxetine"] = "4493",
        ["escitalopram"] = "32937",
        ["citalopram"] = "2556",
        ["paroxetine"] = "32937",
        ["venlafaxine"] = "39786",
        ["duloxetine"] = "72625",
        ["tramadol"] = "10689",
        ["linezolid"] = "190376",
        ["phenelzine"] = "8123",
        ["selegiline"] = "9677",
        ["sumatriptan"] = "10244",
        ["metformin"] = "860975",
        ["iodinated contrast"] = "58477",
        ["simvastatin"] = "36567",
        ["atorvastatin"] = "83367",
        ["lovastatin"] = "6472",
        ["clarithromycin"] = "21106",
        ["erythromycin"] = "4053",
        ["ketoconazole"] = "6135",
        ["itraconazole"] = "28031",
        ["diltiazem"] = "3443",
        ["ciprofloxacin"] = "2551",
        ["levofloxacin"] = "82122",
        ["moxifloxacin"] = "139462",
        ["prednisone"] = "8640",
        ["prednisolone"] = "8638",
        ["dexamethasone"] = "3264",
        ["methylprednisolone"] = "6902",
        ["azithromycin"] = "18631",
        ["amiodarone"] = "703",
        ["sotalol"] = "9959",
        ["haloperidol"] = "5032",
        ["quetiapine"] = "77063",
        ["ondansetron"] = "26225",
        ["amoxicillin"] = "723"
    };

    public IReadOnlyList<DrugInteractionWarning> CheckInteractions(
        IEnumerable<MedicationConcept> existingMeds,
        MedicationConcept proposedMed)
    {
        var warnings = new List<DrugInteractionWarning>();
        if (proposedMed == null || string.IsNullOrWhiteSpace(proposedMed.Name) && string.IsNullOrWhiteSpace(proposedMed.RxNormCode))
            return warnings;

        var proposedRxNorm = proposedMed.RxNormCode ?? ResolveRxNormCode(proposedMed.Name);
        var proposedName = proposedMed.Name.Trim().ToLowerInvariant();

        foreach (var existing in existingMeds)
        {
            if (existing == null) continue;
            var existingRxNorm = existing.RxNormCode ?? ResolveRxNormCode(existing.Name);
            var existingName = existing.Name?.Trim().ToLowerInvariant() ?? string.Empty;

            foreach (var rule in Rules)
            {
                bool matchA = Matches(proposedName, proposedRxNorm, rule.ClassAKeywords, rule.ClassARxNorms) &&
                              Matches(existingName, existingRxNorm, rule.ClassBKeywords, rule.ClassBRxNorms);

                bool matchB = Matches(proposedName, proposedRxNorm, rule.ClassBKeywords, rule.ClassBRxNorms) &&
                              Matches(existingName, existingRxNorm, rule.ClassAKeywords, rule.ClassARxNorms);

                if (matchA || matchB)
                {
                    warnings.Add(new DrugInteractionWarning(
                        Severity: rule.Severity,
                        Drug1: string.IsNullOrWhiteSpace(proposedMed.Name) ? $"RxNorm:{proposedMed.RxNormCode}" : proposedMed.Name,
                        Drug2: string.IsNullOrWhiteSpace(existing.Name) ? $"RxNorm:{existing.RxNormCode}" : existing.Name,
                        Description: rule.Description,
                        ClinicalGuidance: rule.ClinicalGuidance
                    ));
                }
            }
        }

        return warnings;
    }

    public IReadOnlyList<DrugInteractionWarning> CheckInteractions(
        IEnumerable<string> existingMeds,
        string proposedMed)
    {
        var existingConcepts = existingMeds.Select(m => new MedicationConcept(m, ResolveRxNormCode(m)));
        var proposedConcept = new MedicationConcept(proposedMed, ResolveRxNormCode(proposedMed));
        return CheckInteractions(existingConcepts, proposedConcept);
    }

    public string? ResolveRxNormCode(string? medicationName)
    {
        if (string.IsNullOrWhiteSpace(medicationName)) return null;
        var lower = medicationName.Trim().ToLowerInvariant();

        foreach (var (key, code) in NameToRxNormMap)
        {
            if (lower.Contains(key, StringComparison.OrdinalIgnoreCase))
                return code;
        }

        return null;
    }

    private static bool Matches(string medName, string? rxNormCode, string[] keywords, string[] rxNorms)
    {
        if (!string.IsNullOrWhiteSpace(rxNormCode) && rxNorms.Any(r => string.Equals(r, rxNormCode, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!string.IsNullOrWhiteSpace(medName) && keywords.Any(k => medName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }
}
