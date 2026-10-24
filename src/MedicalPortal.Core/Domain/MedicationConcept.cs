namespace MedicalPortal.Core.Domain;

/// <summary>
/// Standardized medication concept structured for clinical terminology interoperability
/// (e.g. RxNorm / RxCUI, ATC, NDC) and pluggable drug databases.
/// </summary>
public record MedicationConcept
{
    public string Name { get; init; } = string.Empty;
    public string? RxNormCode { get; init; }
    public IReadOnlyList<string> DrugClasses { get; init; } = Array.Empty<string>();

    public MedicationConcept() { }

    public MedicationConcept(string name, string? rxNormCode = null, IEnumerable<string>? drugClasses = null)
    {
        Name = name;
        RxNormCode = rxNormCode;
        DrugClasses = drugClasses?.ToArray() ?? Array.Empty<string>();
    }

    public static MedicationConcept FromName(string name) => new(name);

    public static MedicationConcept FromRxNorm(string rxNormCode, string name, params string[] classes) =>
        new(name, rxNormCode, classes);
}
