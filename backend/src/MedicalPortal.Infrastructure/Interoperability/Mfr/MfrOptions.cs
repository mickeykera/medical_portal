namespace MedicalPortal.Infrastructure.Interoperability.Mfr;

public class MfrOptions
{
    public const string SectionName = "Mfr";

    public string BaseUrl { get; set; } = "https://mfr.moh.gov.et/api/";
    public string ApiKey { get; set; } = string.Empty;
    public bool UseFake { get; set; } = false;
}
