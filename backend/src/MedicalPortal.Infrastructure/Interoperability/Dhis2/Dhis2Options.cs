namespace MedicalPortal.Infrastructure.Interoperability.Dhis2;

public class Dhis2Options
{
    public const string SectionName = "Dhis2";

    public string BaseUrl { get; set; } = "https://dhis2.moh.gov.et/";
    public string Username { get; set; } = "hmis_facility_user";
    public string Password { get; set; } = string.Empty;
    public string DefaultDataSetId { get; set; } = "HMIS-MOH-MONTHLY";
    public string ExportHmacSecret { get; set; } = "Ethiopia-DHIS2-HMIS-Offline-Export-Secret-2026";
    public bool UseFake { get; set; } = false;
}
