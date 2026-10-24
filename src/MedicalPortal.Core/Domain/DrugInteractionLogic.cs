namespace MedicalPortal.Core.Domain;

public enum InteractionSeverity
{
    LOW,
    MODERATE,
    HIGH
}

public record DrugInteractionWarning(
    InteractionSeverity Severity,
    string Drug1,
    string Drug2,
    string Description,
    string ClinicalGuidance);

public record PrescriptionSafetyCheckResult(
    bool Allowed,
    bool HasAllergyConflict,
    List<ContraindicationWarning> AllergyWarnings,
    bool HasDrugInteraction,
    List<DrugInteractionWarning> InteractionWarnings);

/// <summary>
/// Clinical safety entry point for drug interaction screening.
/// Delegates evaluation to pluggable <see cref="IInteractionKnowledgeBase"/> implementations.
/// 
/// IMPORTANT: The default knowledge base contains demonstration reference data.
/// It does NOT claim clinical completeness or exhaustiveness.
/// </summary>
public static class DrugInteractionLogic
{
    /// <summary>
    /// Active default knowledge base used when dependency injection is not available.
    /// Can be replaced with a licensed clinical pharmacology provider.
    /// </summary>
    public static IInteractionKnowledgeBase DefaultKnowledgeBase { get; set; } = new DemoInteractionKnowledgeBase();

    /// <summary>
    /// Checks for drug-drug interactions using the configured knowledge base.
    /// </summary>
    public static List<DrugInteractionWarning> CheckDrugInteractions(
        IEnumerable<string> existingMeds,
        string proposedMed)
    {
        return DefaultKnowledgeBase.CheckInteractions(existingMeds, proposedMed).ToList();
    }

    /// <summary>
    /// Checks for drug-drug interactions with full <see cref="MedicationConcept"/> descriptors and explicit knowledge base instance.
    /// </summary>
    public static List<DrugInteractionWarning> CheckDrugInteractions(
        IEnumerable<MedicationConcept> existingMeds,
        MedicationConcept proposedMed,
        IInteractionKnowledgeBase? knowledgeBase = null)
    {
        var kb = knowledgeBase ?? DefaultKnowledgeBase;
        return kb.CheckInteractions(existingMeds, proposedMed).ToList();
    }
}
