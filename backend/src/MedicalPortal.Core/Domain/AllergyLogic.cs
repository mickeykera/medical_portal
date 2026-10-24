using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Domain;

public record AllergyCheckResult(bool Safe, string? BlockReason = null, Allergy? ConflictingAllergy = null);

public record ContraindicationWarning(
    AllergySeverity Severity,
    string Substance,
    string Reason,
    string? Reaction);

/// <summary>
/// Clinical safety entry point for allergy contraindication and cross-reactivity screening.
/// Delegates evaluation to pluggable <see cref="IAllergyKnowledgeBase"/> implementations.
/// 
/// IMPORTANT: The default knowledge base contains demonstration reference data.
/// It does NOT claim clinical completeness or exhaustiveness.
/// </summary>
public static class AllergyLogic
{
    /// <summary>
    /// Active default knowledge base used when dependency injection is not available.
    /// Can be replaced with a licensed clinical pharmacology provider.
    /// </summary>
    public static IAllergyKnowledgeBase DefaultKnowledgeBase { get; set; } = new DemoAllergyKnowledgeBase();

    /// <summary>
    /// Generates contraindication warnings for prescription ordering.
    /// </summary>
    public static List<ContraindicationWarning> CheckContraindications(
        IEnumerable<Allergy> allergies,
        string proposedMedication,
        IEnumerable<string>? medClasses = null)
    {
        return DefaultKnowledgeBase.CheckContraindications(allergies, proposedMedication, medClasses);
    }

    /// <summary>
    /// Generates contraindication warnings with full <see cref="MedicationConcept"/> descriptors and explicit knowledge base instance.
    /// </summary>
    public static List<ContraindicationWarning> CheckContraindications(
        IEnumerable<Allergy> allergies,
        MedicationConcept proposedMedication,
        IAllergyKnowledgeBase? knowledgeBase = null)
    {
        var kb = knowledgeBase ?? DefaultKnowledgeBase;
        return kb.CheckContraindications(allergies, proposedMedication);
    }

    /// <summary>
    /// Evaluates proposed medication by name and drug classes against patient allergy records.
    /// </summary>
    public static AllergyCheckResult CheckPrescription(
        string medicationName,
        IEnumerable<string> medicationClasses,
        IEnumerable<Allergy> patientAllergies)
    {
        return DefaultKnowledgeBase.CheckPrescription(medicationName, medicationClasses, patientAllergies);
    }

    /// <summary>
    /// Evaluates proposed medication concept against patient allergy records with explicit knowledge base instance.
    /// </summary>
    public static AllergyCheckResult CheckPrescription(
        MedicationConcept proposedMedication,
        IEnumerable<Allergy> patientAllergies,
        IAllergyKnowledgeBase? knowledgeBase = null)
    {
        var kb = knowledgeBase ?? DefaultKnowledgeBase;
        return kb.CheckPrescription(proposedMedication, patientAllergies);
    }
}
