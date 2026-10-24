namespace MedicalPortal.Infrastructure.Interoperability.Echis;

public class EchisOptions
{
    public const string SectionName = "Echis";

    /// <summary>
    /// Feature flag controlling eCHIS community health referrals.
    /// In accordance with Target Architecture F, eCHIS sits behind a feature flag.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://echis.moh.gov.et/api/";
    public string ApiKey { get; set; } = string.Empty;
    public string FacilityCode { get; set; } = "ADOT-01";
    public bool UseFake { get; set; } = false;
}
