using MedicalPortal.Core.Entities;

namespace MedicalPortal.Core.Domain;

/// <summary>
/// Pluggable contract for allergy contraindication and cross-reactivity screening.
/// Supports evaluation via RxNorm codes, ATC codes, substance names, or drug class taxonomies.
/// 
/// DISCLAIMER: Safety check implementations provide decision-support data only and do NOT claim
/// clinical completeness or exhaustiveness. Licensed medical practitioners must exercise independent
/// clinical judgment.
/// </summary>
public interface IAllergyKnowledgeBase
{
    /// <summary>
    /// Identifier and semantic version of the underlying allergy rule database.
    /// This value is recorded in compliance audit trails for every clinical safety check.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Descriptive name of the knowledge provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Explicit disclaimer stating the clinical scope and limitation of completeness.
    /// </summary>
    string Disclaimer { get; }

    /// <summary>
    /// Evaluates proposed medication against patient allergy records.
    /// </summary>
    AllergyCheckResult CheckPrescription(
        MedicationConcept proposedMedication,
        IEnumerable<Allergy> patientAllergies);

    /// <summary>
    /// Evaluates proposed medication by name and drug classes against patient allergy records.
    /// </summary>
    AllergyCheckResult CheckPrescription(
        string medicationName,
        IEnumerable<string> medicationClasses,
        IEnumerable<Allergy> patientAllergies);

    /// <summary>
    /// Generates contraindication warnings for prescription ordering.
    /// </summary>
    List<ContraindicationWarning> CheckContraindications(
        IEnumerable<Allergy> allergies,
        MedicationConcept proposedMedication);

    /// <summary>
    /// Generates contraindication warnings for prescription ordering.
    /// </summary>
    List<ContraindicationWarning> CheckContraindications(
        IEnumerable<Allergy> allergies,
        string proposedMedication,
        IEnumerable<string>? medClasses = null);

    /// <summary>
    /// Returns drug classes related by known cross-reactivity.
    /// </summary>
    IReadOnlyList<string> GetRelatedClasses(string drugClassOrCode);

    /// <summary>
    /// Returns known drug classes or cross-reactivity groups for an RxNorm code.
    /// </summary>
    IReadOnlyList<string> GetClassesForRxNorm(string rxNormCode);
}
