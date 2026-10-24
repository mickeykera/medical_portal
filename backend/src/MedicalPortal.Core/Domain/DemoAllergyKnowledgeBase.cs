using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Domain;

/// <summary>
/// DEMONSTRATION / REFERENCE DATA ONLY.
/// This allergy cross-reactivity knowledge base contains sample rules intended solely
/// for automated testing, development, and system demonstration.
/// 
/// IMPORTANT DISCLAIMER:
/// This implementation is NOT clinically complete or exhaustive and MUST NOT be used
/// for real-world clinical decision-making or patient care without integration of a
/// certified, licensed clinical knowledge provider.
/// </summary>
public class DemoAllergyKnowledgeBase : IAllergyKnowledgeBase
{
    public const string DemoVersion = "demo-allergies-2024.1-sample";

    public string Version => DemoVersion;
    public string ProviderName => "Springfield Reference Allergy Knowledge Base (DEMO ONLY)";
    public string Disclaimer => "DEMO DATA ONLY: This dataset contains limited sample cross-reactivity rules for demonstration and automated testing. It does NOT claim clinical completeness or exhaustiveness. Always use an accredited clinical pharmacology database for production.";

    private static readonly Dictionary<string, string[]> RelatedClassMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Penicillins and beta-lactams
        ["J01CA"] = new[] { "Beta-lactam (penicillin)", "Penicillin", "J01CA", "J01C", "J01" },
        ["J01CA01"] = new[] { "Beta-lactam (penicillin)", "Penicillin", "J01CA", "J01C" },
        ["Beta-lactam (penicillin)"] = new[] { "Beta-lactam (penicillin)", "Penicillin", "J01CA", "J01C" },
        ["Penicillin"] = new[] { "Beta-lactam (penicillin)", "Penicillin", "J01CA", "J01C" },

        // Sulfonamides
        ["SULFONAMIDE"] = new[] { "Sulfonamide", "Sulfonamides", "SULFONAMIDE", "J01EE" },
        ["Sulfonamide"] = new[] { "Sulfonamide", "Sulfonamides", "SULFONAMIDE", "J01EE" },
        ["Sulfonamides"] = new[] { "Sulfonamide", "Sulfonamides", "SULFONAMIDE", "J01EE" },

        // Contrast
        ["IODINATED_CONTRAST"] = new[] { "Iodinated contrast", "IODINATED_CONTRAST" },
        ["Iodinated contrast"] = new[] { "Iodinated contrast", "IODINATED_CONTRAST" }
    };

    private static readonly Dictionary<string, string[]> RxNormToClassesMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["723"] = new[] { "J01CA", "J01CA01", "Beta-lactam (penicillin)", "Penicillin" }, // Amoxicillin
        ["313782"] = new[] { "J01CA", "J01CA01", "Beta-lactam (penicillin)", "Penicillin" },
        ["733"] = new[] { "J01CA", "Beta-lactam (penicillin)", "Penicillin" }, // Ampicillin
        ["313783"] = new[] { "J01CA", "Beta-lactam (penicillin)", "Penicillin" },
        ["7980"] = new[] { "J01CA", "Beta-lactam (penicillin)", "Penicillin" }, // Penicillin G
        ["7984"] = new[] { "J01CA", "Beta-lactam (penicillin)", "Penicillin" }, // Penicillin V
        ["10224"] = new[] { "SULFONAMIDE", "Sulfonamide", "J01EE" }, // Sulfamethoxazole
        ["10227"] = new[] { "SULFONAMIDE", "Sulfonamide", "J01EE" }, // TMP/SMX
        ["58477"] = new[] { "IODINATED_CONTRAST", "Iodinated contrast" } // Contrast
    };

    public IReadOnlyList<string> GetRelatedClasses(string drugClassOrCode)
    {
        if (string.IsNullOrWhiteSpace(drugClassOrCode)) return Array.Empty<string>();
        if (RelatedClassMap.TryGetValue(drugClassOrCode, out var list))
            return list;
        return Array.Empty<string>();
    }

    public IReadOnlyList<string> GetClassesForRxNorm(string rxNormCode)
    {
        if (string.IsNullOrWhiteSpace(rxNormCode)) return Array.Empty<string>();
        if (RxNormToClassesMap.TryGetValue(rxNormCode.Trim(), out var classes))
            return classes;
        return Array.Empty<string>();
    }

    public List<ContraindicationWarning> CheckContraindications(
        IEnumerable<Allergy> allergies,
        MedicationConcept proposedMedication)
    {
        var list = new List<ContraindicationWarning>();
        var classes = new List<string>(proposedMedication.DrugClasses);

        if (!string.IsNullOrWhiteSpace(proposedMedication.RxNormCode))
        {
            classes.AddRange(GetClassesForRxNorm(proposedMedication.RxNormCode));
        }

        var medName = proposedMedication.Name ?? string.Empty;
        if (medName.Contains("amoxicillin", StringComparison.OrdinalIgnoreCase) ||
            medName.Contains("ampicillin", StringComparison.OrdinalIgnoreCase) ||
            medName.Contains("penicillin", StringComparison.OrdinalIgnoreCase))
        {
            classes.Add("J01CA");
            classes.Add("Beta-lactam (penicillin)");
        }

        var result = CheckPrescription(medName, classes, allergies);
        if (!result.Safe && result.ConflictingAllergy != null)
        {
            list.Add(new ContraindicationWarning(
                Severity: result.ConflictingAllergy.Severity,
                Substance: result.ConflictingAllergy.Substance,
                Reason: result.BlockReason ?? "Contraindicated due to documented allergy",
                Reaction: result.ConflictingAllergy.Reaction
            ));
        }

        return list;
    }

    public List<ContraindicationWarning> CheckContraindications(
        IEnumerable<Allergy> allergies,
        string proposedMedication,
        IEnumerable<string>? medClasses = null)
    {
        var concept = new MedicationConcept(proposedMedication, null, medClasses);
        return CheckContraindications(allergies, concept);
    }

    public AllergyCheckResult CheckPrescription(
        MedicationConcept proposedMedication,
        IEnumerable<Allergy> patientAllergies)
    {
        var classes = new List<string>(proposedMedication.DrugClasses);
        if (!string.IsNullOrWhiteSpace(proposedMedication.RxNormCode))
        {
            classes.AddRange(GetClassesForRxNorm(proposedMedication.RxNormCode));
        }

        return CheckPrescription(proposedMedication.Name, classes, patientAllergies);
    }

    public AllergyCheckResult CheckPrescription(
        string medicationName,
        IEnumerable<string> medicationClasses,
        IEnumerable<Allergy> patientAllergies)
    {
        var medClassesList = medicationClasses.ToList();

        foreach (var allergy in patientAllergies)
        {
            if (allergy.Tombstone) continue;

            // Direct substance match
            if (!string.IsNullOrWhiteSpace(medicationName) &&
                (string.Equals(allergy.Substance, medicationName, StringComparison.OrdinalIgnoreCase) ||
                 medicationName.Contains(allergy.Substance, StringComparison.OrdinalIgnoreCase)))
            {
                return new(false,
                    $"Contraindicated: Patient has documented {allergy.Severity.ToString().ToLowerInvariant()} allergy to {allergy.Substance} ({allergy.Reaction ?? "no reaction specified"}).",
                    allergy);
            }

            // Class code / class match
            var allergyClass = allergy.DrugClassCode ?? allergy.DrugClass;
            if (!string.IsNullOrEmpty(allergyClass))
            {
                // Check if medication classes contain allergy class
                if (medClassesList.Any(mc => string.Equals(mc, allergyClass, StringComparison.OrdinalIgnoreCase) ||
                                             allergyClass.StartsWith(mc, StringComparison.OrdinalIgnoreCase) ||
                                             mc.StartsWith(allergyClass, StringComparison.OrdinalIgnoreCase)))
                {
                    return new(false,
                        $"Cross-reactivity warning: {medicationName} belongs to drug class '{allergy.DrugClass}', conflicting with patient's allergy to {allergy.Substance}.",
                        allergy);
                }

                // Check related classes from mapping
                if (RelatedClassMap.TryGetValue(allergyClass, out var related))
                {
                    if (medClassesList.Any(mc => related.Any(r => string.Equals(r, mc, StringComparison.OrdinalIgnoreCase))))
                    {
                        return new(false,
                            $"Cross-reactivity risk: {medicationName} shares cross-reactivity with {allergy.Substance} ({allergy.DrugClass}).",
                            allergy);
                    }
                }
            }
        }

        return new(true);
    }
}
