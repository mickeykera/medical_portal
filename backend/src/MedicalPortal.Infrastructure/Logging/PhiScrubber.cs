using System.Collections;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Logging;

public interface IPhiScrubber
{
    string ScrubText(string? input);
    object? ScrubValue(string key, object? value);
    IDictionary<string, object?> ScrubProperties(IEnumerable<KeyValuePair<string, object?>> properties);
    Exception? ScrubException(Exception? exception);
}

public class PhiScrubber : IPhiScrubber
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "mrn", "patientmrn", "ssn", "dob", "dateofbirth", "birthdate",
        "patientname", "firstname", "lastname", "fullname", "patient",
        "notes", "patientnotes", "clinicalnotes", "freetext", "body", "reasonforvisit", "clinicalvalue",
        "address", "addressline1", "addressline2", "street", "postalcode", "zip",
        "email", "phone", "phonenumber", "emergencycontactname", "emergencycontactphone",
        "secret", "password", "token", "jwt", "totpsecret", "mfasecret", "recoverycode", "refreshtoken",
        "fayya", "nationalpatientid", "nationalid", "healthextensionworkerid", "fayyanationalid"
    };

    // Regex for Social Security Numbers: 123-45-6789 or 123 45 6789 or 9 digits
    private static readonly Regex SsnRegex = new(
        @"\b\d{3}[-\s]\d{2}[-\s]\d{4}\b",
        RegexOptions.Compiled);

    // Regex for Dates of Birth: YYYY-MM-DD or MM/DD/YYYY
    private static readonly Regex IsoDateRegex = new(
        @"\b(19\d\d|20\d\d)[-/](0[1-9]|1[0-2])[-/](0[1-9]|[12]\d|3[01])\b",
        RegexOptions.Compiled);

    private static readonly Regex UsDateRegex = new(
        @"\b(0[1-9]|1[0-2])[-/](0[1-9]|[12]\d|3[01])[-/](19\d\d|20\d\d)\b",
        RegexOptions.Compiled);

    // Regex for MRN: e.g. MRN-123456, MRN12345, mrn: 987654
    private static readonly Regex MrnRegex = new(
        @"\b(?:MRN|mrn)[-:\s#]*[A-Za-z0-9]{4,12}\b",
        RegexOptions.Compiled);

    // Regex for Email Addresses
    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    // Regex for North American Phone Numbers: (123) 456-7890, 123-456-7890, +1 123 456 7890
    private static readonly Regex PhoneRegex = new(
        @"(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}\b",
        RegexOptions.Compiled);

    // Regex for Ethiopian Phone Numbers: +2519..., 09..., +2517..., 07...
    private static readonly Regex EthiopianPhoneRegex = new(
        @"(?:\+?251|0)[79]\d{8}\b",
        RegexOptions.Compiled);

    // Regex for Ethiopian Fayya National Patient IDs: ET-FAYYA-...
    private static readonly Regex FayyaIdRegex = new(
        @"\bET-FAYYA-[A-Za-z0-9-]+\b",
        RegexOptions.Compiled);

    public string ScrubText(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var scrubbed = input;
        scrubbed = SsnRegex.Replace(scrubbed, "[SSN-REDACTED]");
        scrubbed = IsoDateRegex.Replace(scrubbed, "[DOB-REDACTED]");
        scrubbed = UsDateRegex.Replace(scrubbed, "[DOB-REDACTED]");
        scrubbed = MrnRegex.Replace(scrubbed, "[MRN-REDACTED]");
        scrubbed = EmailRegex.Replace(scrubbed, "[EMAIL-REDACTED]");
        scrubbed = PhoneRegex.Replace(scrubbed, "[PHONE-REDACTED]");
        scrubbed = EthiopianPhoneRegex.Replace(scrubbed, "[PHONE-REDACTED]");
        scrubbed = FayyaIdRegex.Replace(scrubbed, "[NATIONALID-REDACTED]");

        return scrubbed;
    }

    public object? ScrubValue(string key, object? value)
    {
        if (value == null) return null;

        if (SensitiveKeys.Contains(key))
        {
            return "[REDACTED]";
        }

        if (value is string strVal)
        {
            return ScrubText(strVal);
        }

        if (value is IEnumerable<KeyValuePair<string, object?>> kvpEnum)
        {
            return ScrubProperties(kvpEnum);
        }

        return value;
    }

    public IDictionary<string, object?> ScrubProperties(IEnumerable<KeyValuePair<string, object?>> properties)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in properties)
        {
            dict[kvp.Key] = ScrubValue(kvp.Key, kvp.Value);
        }
        return dict;
    }

    public Exception? ScrubException(Exception? exception)
    {
        if (exception == null) return null;

        var scrubbedMessage = ScrubText(exception.Message);
        var scrubbedStackTrace = ScrubText(exception.StackTrace);

        // Return a sanitized wrapper exception for safe logging
        return new PhiSanitizedException(scrubbedMessage, scrubbedStackTrace, ScrubException(exception.InnerException));
    }
}

public class PhiSanitizedException : Exception
{
    private readonly string? _customStackTrace;

    public PhiSanitizedException(string message, string? stackTrace, Exception? innerException)
        : base(message, innerException)
    {
        _customStackTrace = stackTrace;
    }

    public override string? StackTrace => _customStackTrace ?? base.StackTrace;
}
