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
/// IMPORTANT ARCHITECTURAL BOUNDARY:
/// Canonical clinical decision support and medication ordering is mastered in the Clinical Core (OpenMRS 2.8+ / O3).
/// The portal-level rules in this class serve solely as demonstration reference data for testing and mock environments.
/// </summary>
public static class DrugInteractionLogic
{
    /// <summary>
    /// Master boundary notice indicating that canonical CDS is governed by OpenMRS order entry.
    /// </summary>
    public const string ClinicalCoreMasteryNotice = "Canonical clinical decision support is mastered in OpenMRS 2.8+ / O3. Portal safety screening uses demonstration reference data.";

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
