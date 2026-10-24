using System.Globalization;

namespace MedicalPortal.Core.Domain;

public static class ClinicalFormatting
{
    public static int AgeFrom(DateTime dob, DateTime? asOf = null)
    {
        var target = asOf ?? DateTime.UtcNow;
        var age = target.Year - dob.Year;
        if (target.Month < dob.Month || (target.Month == dob.Month && target.Day < dob.Day))
        {
            age--;
        }
        return Math.Max(0, age);
    }

    public static string ToIsoDate(DateTime? dt) =>
        dt.HasValue ? dt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

    public static string FormatCents(int cents) =>
        (cents / 100.0m).ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    public static string ToneFor(string status)
    {
        var s = status.ToUpperInvariant();
        if (s is "CONFIRMED" or "PAID" or "UP_TO_DATE" or "SUCCEEDED" or "SEEN" or "SIGNED" or "IN_STOCK" or "IN_ROOM")
            return "good";
        if (s is "REQUESTED" or "DUE" or "PARTIALLY_PAID" or "OPEN" or "PENDING" or "AWAITING_CLINICIAN" or "WAITING" or "IN_TRIAGE" or "IN_PROGRESS")
            return "warn";
        if (s is "OVERDUE" or "HIGH" or "LOW" or "DENIED" or "FAILED" or "OUT_OF_STOCK" or "BLOCKED")
            return "bad";
        return string.Empty;
    }
}
