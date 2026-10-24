namespace MedicalPortal.Core.Domain;

/// <summary>
/// Pluggable contract for drug-drug interaction screening.
/// Implementations may range from demonstration/reference datasets to licensed commercial pharmacology
/// databases (e.g. First Databank, Wolters Kluwer Medi-Span, Cerner Multum, or NLM RxNorm APIs).
/// 
/// DISCLAIMER: Safety check implementations provide decision-support data only and do NOT claim
/// clinical completeness or exhaustiveness. Licensed medical practitioners must exercise independent
/// clinical judgment.
/// </summary>
public interface IInteractionKnowledgeBase
{
    /// <summary>
    /// Identifier and semantic version of the underlying drug interaction rule database.
    /// This value is recorded in compliance audit trails for every clinical safety check.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Descriptive name of the knowledge provider (e.g. "Springfield Reference Knowledge Base (Demo)", "First Databank NDDF").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Explicit disclaimer stating the clinical scope and limitation of completeness.
    /// </summary>
    string Disclaimer { get; }

    /// <summary>
    /// Checks for drug-drug interactions between currently active medications and a proposed medication concept,
    /// matching via standardized RxNorm codes, drug classes, or generic/brand keywords.
    /// </summary>
    IReadOnlyList<DrugInteractionWarning> CheckInteractions(
        IEnumerable<MedicationConcept> existingMeds,
        MedicationConcept proposedMed);

    /// <summary>
    /// Convenience helper for string medication names.
    /// </summary>
    IReadOnlyList<DrugInteractionWarning> CheckInteractions(
        IEnumerable<string> existingMeds,
        string proposedMed);

    /// <summary>
    /// Resolves or enriches a medication name to an RxNorm code if cataloged by this knowledge base.
    /// </summary>
    string? ResolveRxNormCode(string? medicationName);
}
