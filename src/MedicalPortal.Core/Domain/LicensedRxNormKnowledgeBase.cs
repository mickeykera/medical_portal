using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Domain;

/// <summary>
/// Rule structure for licensed drug databases matching interactions via RxNorm concept codes (RxCUIs).
/// </summary>
public record RxNormInteractionRule(
    string RuleId,
    IReadOnlyList<string> RxNormCodesA,
    IReadOnlyList<string> RxNormCodesB,
    InteractionSeverity Severity,
    string Description,
    string ClinicalGuidance,
    string? EvidenceLevel = null,
    string? MonographUrl = null);

/// <summary>
/// Rule structure for licensed allergy cross-reactivity databases matching contraindications via RxNorm codes.
/// </summary>
public record RxNormAllergyRule(
    string RuleId,
    string AllergenClassOrCode,
    IReadOnlyList<string> ContraindicatedRxNormCodes,
    AllergySeverity Severity,
    string Reason,
    string? Reaction = null);

/// <summary>
/// Pluggable adapter for licensed clinical pharmacology knowledge bases (e.g. First Databank,
/// Wolters Kluwer Medi-Span, Cerner Multum, or NLM RxNorm Drug Interaction APIs).
/// Evaluates clinical safety checks primarily through standardized RxNorm Concept Unique Identifiers (RxCUIs).
/// 
/// DISCLAIMER: Provides decision-support warnings based on licensed data feeds.
/// Does NOT claim clinical completeness or exhaustiveness for all clinical contexts.
/// </summary>
public class LicensedRxNormKnowledgeBase : IInteractionKnowledgeBase, IAllergyKnowledgeBase
{
    private readonly List<RxNormInteractionRule> _interactionRules = new();
    private readonly List<RxNormAllergyRule> _allergyRules = new();
    private readonly Dictionary<string, string> _nameToRxNorm = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _rxNormToClasses = new(StringComparer.OrdinalIgnoreCase);

    public string Version { get; }
    public string ProviderName { get; }
    public string Disclaimer { get; }

    public LicensedRxNormKnowledgeBase(
        string version,
        string providerName,
        string? disclaimer = null)
    {
        Version = version;
        ProviderName = providerName;
        Disclaimer = disclaimer ?? $"Licensed pharmacology database provided by {providerName} (v{version}). Decision support only; does not claim clinical completeness.";
    }

    public void RegisterInteractionRule(RxNormInteractionRule rule)
    {
        _interactionRules.Add(rule);
    }

    public void RegisterAllergyRule(RxNormAllergyRule rule)
    {
        _allergyRules.Add(rule);
        foreach (var rxcui in rule.ContraindicatedRxNormCodes)
        {
            if (!_rxNormToClasses.TryGetValue(rxcui, out var classes))
            {
                classes = new List<string>();
                _rxNormToClasses[rxcui] = classes;
            }
            if (!classes.Contains(rule.AllergenClassOrCode))
            {
                classes.Add(rule.AllergenClassOrCode);
            }
        }
    }

    public void MapNameToRxNorm(string medicationName, string rxNormCode)
    {
        _nameToRxNorm[medicationName] = rxNormCode;
    }

    #region IInteractionKnowledgeBase

    public IReadOnlyList<DrugInteractionWarning> CheckInteractions(
        IEnumerable<MedicationConcept> existingMeds,
        MedicationConcept proposedMed)
    {
        var warnings = new List<DrugInteractionWarning>();
        var proposedRxNorm = proposedMed.RxNormCode ?? ResolveRxNormCode(proposedMed.Name);
        if (string.IsNullOrWhiteSpace(proposedRxNorm))
            return warnings;

        foreach (var existing in existingMeds)
        {
            if (existing == null) continue;
            var existingRxNorm = existing.RxNormCode ?? ResolveRxNormCode(existing.Name);
            if (string.IsNullOrWhiteSpace(existingRxNorm)) continue;

            foreach (var rule in _interactionRules)
            {
                bool matchA = rule.RxNormCodesA.Contains(proposedRxNorm, StringComparer.OrdinalIgnoreCase) &&
                              rule.RxNormCodesB.Contains(existingRxNorm, StringComparer.OrdinalIgnoreCase);

                bool matchB = rule.RxNormCodesB.Contains(proposedRxNorm, StringComparer.OrdinalIgnoreCase) &&
                              rule.RxNormCodesA.Contains(existingRxNorm, StringComparer.OrdinalIgnoreCase);

                if (matchA || matchB)
                {
                    warnings.Add(new DrugInteractionWarning(
                        Severity: rule.Severity,
                        Drug1: string.IsNullOrWhiteSpace(proposedMed.Name) ? $"RxNorm:{proposedRxNorm}" : proposedMed.Name,
                        Drug2: string.IsNullOrWhiteSpace(existing.Name) ? $"RxNorm:{existingRxNorm}" : existing.Name,
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
        if (_nameToRxNorm.TryGetValue(medicationName.Trim(), out var code))
            return code;

        foreach (var (key, val) in _nameToRxNorm)
        {
            if (medicationName.Contains(key, StringComparison.OrdinalIgnoreCase))
                return val;
        }

        return null;
    }

    #endregion

    #region IAllergyKnowledgeBase

    public AllergyCheckResult CheckPrescription(
        MedicationConcept proposedMedication,
        IEnumerable<Allergy> patientAllergies)
    {
        var rxNorm = proposedMedication.RxNormCode ?? ResolveRxNormCode(proposedMedication.Name);

        foreach (var allergy in patientAllergies)
        {
            if (allergy.Tombstone) continue;

            // Direct substance match
            if (!string.IsNullOrWhiteSpace(proposedMedication.Name) &&
                (string.Equals(allergy.Substance, proposedMedication.Name, StringComparison.OrdinalIgnoreCase) ||
                 proposedMedication.Name.Contains(allergy.Substance, StringComparison.OrdinalIgnoreCase)))
            {
                return new(false,
                    $"Contraindicated: Patient has documented {allergy.Severity.ToString().ToLowerInvariant()} allergy to {allergy.Substance}.",
                    allergy);
            }

            if (!string.IsNullOrWhiteSpace(rxNorm))
            {
                var allergyClass = allergy.DrugClassCode ?? allergy.DrugClass;
                var matchingRule = _allergyRules.FirstOrDefault(r =>
                    r.ContraindicatedRxNormCodes.Contains(rxNorm, StringComparer.OrdinalIgnoreCase) &&
                    (string.Equals(r.AllergenClassOrCode, allergyClass, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.AllergenClassOrCode, allergy.Substance, StringComparison.OrdinalIgnoreCase)));

                if (matchingRule != null)
                {
                    return new(false,
                        $"Cross-reactivity warning: RxNorm {rxNorm} ({proposedMedication.Name}) conflicts with allergy to {allergy.Substance} ({matchingRule.Reason}).",
                        allergy);
                }
            }
        }

        return new(true);
    }

    public AllergyCheckResult CheckPrescription(
        string medicationName,
        IEnumerable<string> medicationClasses,
        IEnumerable<Allergy> patientAllergies)
    {
        var concept = new MedicationConcept(medicationName, ResolveRxNormCode(medicationName), medicationClasses);
        return CheckPrescription(concept, patientAllergies);
    }

    public List<ContraindicationWarning> CheckContraindications(
        IEnumerable<Allergy> allergies,
        MedicationConcept proposedMedication)
    {
        var list = new List<ContraindicationWarning>();
        var result = CheckPrescription(proposedMedication, allergies);
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
        var concept = new MedicationConcept(proposedMedication, ResolveRxNormCode(proposedMedication), medClasses);
        return CheckContraindications(allergies, concept);
    }

    public IReadOnlyList<string> GetRelatedClasses(string drugClassOrCode)
    {
        return Array.Empty<string>();
    }

    public IReadOnlyList<string> GetClassesForRxNorm(string rxNormCode)
    {
        if (_rxNormToClasses.TryGetValue(rxNormCode, out var classes))
            return classes;
        return Array.Empty<string>();
    }

    #endregion
}
