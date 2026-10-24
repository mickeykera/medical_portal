namespace MedicalPortal.Core.Common;

public record LanguageInfo(string Code, string Name, string NativeName, string Direction = "ltr");

/// <summary>
/// Authoritative translation catalog for English (en), Amharic (am), and Afaan Oromoo (om).
/// Covers clinical, patient portal, scheduling, administrative, calendar, and system messages.
/// Zero external runtime CDN dependencies.
/// </summary>
public static class LocalizationCatalog
{
    public static readonly IReadOnlyList<LanguageInfo> SupportedLanguages = new[]
    {
        new LanguageInfo("en", "English", "English"),
        new LanguageInfo("am", "Amharic", "አማርኛ"),
        new LanguageInfo("om", "Afaan Oromoo", "Afaan Oromoo")
    };

    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        // Application
        ["app.title"] = "Medical Portal",

        // Navigation & Shell
        ["nav.overview"] = "Overview",
        ["nav.appointments"] = "Appointments",
        ["nav.messages"] = "Messages",
        ["nav.records"] = "Health Records",
        ["nav.billing"] = "Billing & Payments",
        ["nav.refills"] = "Medication Refills",
        ["nav.forms"] = "Intake Forms",
        ["nav.sharing"] = "Record Sharing",
        ["nav.family"] = "Family & Proxy",
        ["nav.settings"] = "Settings",
        ["nav.admin"] = "Admin Console",
        ["nav.breakGlass"] = "Emergency Override",
        ["nav.logout"] = "Sign Out",
        ["nav.language"] = "Language",

        // Common Actions
        ["action.save"] = "Save",
        ["action.cancel"] = "Cancel",
        ["action.submit"] = "Submit",
        ["action.delete"] = "Delete",
        ["action.edit"] = "Edit",
        ["action.confirm"] = "Confirm",
        ["action.close"] = "Close",
        ["action.back"] = "Back",
        ["action.search"] = "Search",
        ["action.filter"] = "Filter",
        ["action.refresh"] = "Refresh",
        ["action.loading"] = "Loading...",

        // Clinical & Health
        ["clinical.allergies"] = "Allergies & Adverse Reactions",
        ["clinical.conditions"] = "Health Conditions",
        ["clinical.medications"] = "Current Medications",
        ["clinical.vitals"] = "Vital Signs",
        ["clinical.labs"] = "Laboratory Results",
        ["clinical.encounters"] = "Clinical Encounters",
        ["clinical.immunizations"] = "Immunizations",
        ["clinical.noRecords"] = "No clinical records found.",
        ["clinical.recordedAt"] = "Recorded On",
        ["clinical.severity"] = "Severity",
        ["clinical.reaction"] = "Reaction",

        // Appointments & Scheduling
        ["appt.title"] = "Schedule Appointment",
        ["appt.type"] = "Appointment Type",
        ["appt.provider"] = "Healthcare Provider",
        ["appt.location"] = "Facility & Department",
        ["appt.date"] = "Date",
        ["appt.time"] = "Time",
        ["appt.reason"] = "Reason for Visit",
        ["appt.bookNow"] = "Book Appointment",
        ["appt.cancel"] = "Cancel Appointment",
        ["appt.reschedule"] = "Reschedule",
        ["appt.status.scheduled"] = "Scheduled",
        ["appt.status.completed"] = "Completed",
        ["appt.status.cancelled"] = "Cancelled",
        ["appt.dualCalendarNotice"] = "Displayed in Ethiopian and Gregorian dates.",

        // Billing & Payments
        ["billing.statements"] = "Billing Statements",
        ["billing.amountDue"] = "Amount Due",
        ["billing.payNow"] = "Pay Now",
        ["billing.paymentPlan"] = "Set Up Installment Plan",
        ["billing.receipt"] = "Receipt",
        ["billing.status.paid"] = "Paid",
        ["billing.status.pending"] = "Pending",
        ["billing.status.overdue"] = "Overdue",

        // Messages & Notifications
        ["msg.inbox"] = "Message Inbox",
        ["msg.compose"] = "Compose Message",
        ["msg.send"] = "Send Message",
        ["msg.recipient"] = "Recipient",
        ["msg.subject"] = "Subject",
        ["msg.body"] = "Message Body",

        // Emergency Break-Glass
        ["breakglass.title"] = "Emergency Clinical Override",
        ["breakglass.warning"] = "Warning: Emergency override access is strictly monitored and audited. Use solely for acute life-threatening situations.",
        ["breakglass.justification"] = "Clinical Justification",
        ["breakglass.activate"] = "Initiate Override",
        ["breakglass.conclude"] = "Conclude Override",

        // Ethiopian Calendar
        ["cal.enkutatash"] = "Enkutatash (Ethiopian New Year)",
        ["cal.pagume"] = "Pagume (13th Month)",
        ["cal.fiscalYear"] = "Ethiopian Fiscal Year",
        ["cal.quarter"] = "Fiscal Quarter"
    };

    public static readonly IReadOnlyDictionary<string, string> Amharic = new Dictionary<string, string>
    {
        // Application
        ["app.title"] = "የህክምና ፖርታል",

        // Navigation & Shell
        ["nav.overview"] = "አጠቃላይ እይታ",
        ["nav.appointments"] = "የቀጠሮዎች መርሃ-ግብር",
        ["nav.messages"] = "መልዕክቶች",
        ["nav.records"] = "የጤና መዝገብ",
        ["nav.billing"] = "ክፍያ እና ሂሳብ",
        ["nav.refills"] = "የመድኃኒት ማዘዣ እድሳት",
        ["nav.forms"] = "የቅበላ ቅጾች",
        ["nav.sharing"] = "መዝገብ ማጋራት",
        ["nav.family"] = "የቤተሰብ እና ተወካይ",
        ["nav.settings"] = "ቅንብሮች",
        ["nav.admin"] = "የስርዓት አስተዳደር",
        ["nav.breakGlass"] = "የድንገተኛ ጊዜ ፍቃድ",
        ["nav.logout"] = "ውጣ",
        ["nav.language"] = "ቋንቋ",

        // Common Actions
        ["action.save"] = "አስቀምጥ",
        ["action.cancel"] = "ይቅር",
        ["action.submit"] = "አስገባ",
        ["action.delete"] = "ሰርዝ",
        ["action.edit"] = "አስተካክል",
        ["action.confirm"] = "አረጋግጥ",
        ["action.close"] = "ዝጋ",
        ["action.back"] = "ተመለስ",
        ["action.search"] = "ፈልግ",
        ["action.filter"] = "አጣራ",
        ["action.refresh"] = "አድስ",
        ["action.loading"] = "በመጫን ላይ...",

        // Clinical & Health
        ["clinical.allergies"] = "የአለርጂ እና ተቃራኒ ምላሾች",
        ["clinical.conditions"] = "የህመም ሁኔታዎች",
        ["clinical.medications"] = "ወቅታዊ መድኃኒቶች",
        ["clinical.vitals"] = "አስፈላጊ የሰውነት ምልክቶች",
        ["clinical.labs"] = "የላብራቶሪ ምርመራ ውጤቶች",
        ["clinical.encounters"] = "የህክምና ክትትል ታሪክ",
        ["clinical.immunizations"] = "ክትባቶች",
        ["clinical.noRecords"] = "ምንም የህክምና መዝገብ አልተገኘም።",
        ["clinical.recordedAt"] = "የተመዘገበበት ቀን",
        ["clinical.severity"] = "የህመሙ ደረጃ",
        ["clinical.reaction"] = "የታየው ምላሽ",

        // Appointments & Scheduling
        ["appt.title"] = "ቀጠሮ ይያዙ",
        ["appt.type"] = "የቀጠሮ አይነት",
        ["appt.provider"] = "የህክምና ባለሙያ",
        ["appt.location"] = "የህክምና ተቋም እና ክፍል",
        ["appt.date"] = "ቀን",
        ["appt.time"] = "ሰዓት",
        ["appt.reason"] = "የጉብኝቱ ምክንያት",
        ["appt.bookNow"] = "ቀጠሮውን አረጋግጥ",
        ["appt.cancel"] = "ቀጠሮውን ሰርዝ",
        ["appt.reschedule"] = "ቀጠሮውን አዛውር",
        ["appt.status.scheduled"] = "ተይዟል",
        ["appt.status.completed"] = "ተጠናቋል",
        ["appt.status.cancelled"] = "ተሰርዟል",
        ["appt.dualCalendarNotice"] = "በኢትዮጵያ እና በፈረንጆች አቆጣጠር ይታያል።",

        // Billing & Payments
        ["billing.statements"] = "የክፍያ ዝርዝር",
        ["billing.amountDue"] = "የሚፈለግ ክፍያ",
        ["billing.payNow"] = "አሁን ክፈል",
        ["billing.paymentPlan"] = "በክፍልፍል የመክፈያ እቅድ",
        ["billing.receipt"] = "ደረሰኝ",
        ["billing.status.paid"] = "ተከፍሏል",
        ["billing.status.pending"] = "በመጠባበቅ ላይ",
        ["billing.status.overdue"] = "ጊዜው ያለፈበት",

        // Messages & Notifications
        ["msg.inbox"] = "የመልዕክት ሳጥን",
        ["msg.compose"] = "አዲስ መልዕክት ጻፍ",
        ["msg.send"] = "ላክ",
        ["msg.recipient"] = "ተቀባይ",
        ["msg.subject"] = "ርዕስ",
        ["msg.body"] = "የመልዕክቱ ይዘት",

        // Emergency Break-Glass
        ["breakglass.title"] = "የድንገተኛ ጊዜ የህክምና ፍቃድ",
        ["breakglass.warning"] = "ማስጠንቀቂያ፡ ይህ የድንገተኛ ፍቃድ በጥብቅ ክትትል የሚደረግበት ነው። ለህይወት አድን አስቸኳይ ሁኔታ ብቻ ይጠቀሙ።",
        ["breakglass.justification"] = "የህክምና ምክንያት",
        ["breakglass.activate"] = "ፍቃዱን ጀምር",
        ["breakglass.conclude"] = "ፍቃዱን አጠናቅቅ",

        // Ethiopian Calendar
        ["cal.enkutatash"] = "እንቁጣጣሽ (የዘመን መለወጫ)",
        ["cal.pagume"] = "ጳጉሜ (13ኛው ወር)",
        ["cal.fiscalYear"] = "የኢትዮጵያ የበጀት ዓመት",
        ["cal.quarter"] = "የበጀት ሩብ ዓመት"
    };

    public static readonly IReadOnlyDictionary<string, string> Oromo = new Dictionary<string, string>
    {
        // Application
        ["app.title"] = "Poortaalii Yaalaa",

        // Navigation & Shell
        ["nav.overview"] = "Waliigala",
        ["nav.appointments"] = "Beellama",
        ["nav.messages"] = "Ergaawwan",
        ["nav.records"] = "Galmee Fayyaa",
        ["nav.billing"] = "Kaffaltii fi Baasii",
        ["nav.refills"] = "Qoricha Haaromsuu",
        ["nav.forms"] = "Unkaalee Qorannoo",
        ["nav.sharing"] = "Galmee Qooduu",
        ["nav.family"] = "Maatii fi Bakka-bu'aa",
        ["nav.settings"] = "Qindaa'inoota",
        ["nav.admin"] = "Bulchiinsa Sirnaa",
        ["nav.breakGlass"] = "Hayyama Yeroo Balaa",
        ["nav.logout"] = "Bahi",
        ["nav.language"] = "Afaan",

        // Common Actions
        ["action.save"] = "Olkaayi",
        ["action.cancel"] = "Dhiisi",
        ["action.submit"] = "Galchi",
        ["action.delete"] = "Haqi",
        ["action.edit"] = "Gulaali",
        ["action.confirm"] = "Mirkaneessi",
        ["action.close"] = "Cufi",
        ["action.back"] = "Deebi'i",
        ["action.search"] = "Barbaadi",
        ["action.filter"] = "Calali",
        ["action.refresh"] = "Haaromsi",
        ["action.loading"] = "Fe'amaa jira...",

        // Clinical & Health
        ["clinical.allergies"] = "Alarjii fi Miidhaa Qorichaa",
        ["clinical.conditions"] = "Haalota Dhukkubaa",
        ["clinical.medications"] = "Qorichoota Ammaa",
        ["clinical.vitals"] = "Mallattoolee Lubbuu",
        ["clinical.labs"] = "Bu'aalee Laabiraatoorii",
        ["clinical.encounters"] = "Seenaa Wal'aansaa",
        ["clinical.immunizations"] = "Talaallii",
        ["clinical.noRecords"] = "Galmeen fayyaa hin argamne.",
        ["clinical.recordedAt"] = "Guyyaa Galmaa'e",
        ["clinical.severity"] = "Sadarkaa Miidhaa",
        ["clinical.reaction"] = "Murtii/Deebii",

        // Appointments & Scheduling
        ["appt.title"] = "Beellama Qabadhaa",
        ["appt.type"] = "Gosa Beellamaa",
        ["appt.provider"] = "Ogeessa Fayyaa",
        ["appt.location"] = "Dhaabbata fi Kutaa",
        ["appt.date"] = "Guyyaa",
        ["appt.time"] = "Sa'aatii",
        ["appt.reason"] = "Sababa Daawwannaaf",
        ["appt.bookNow"] = "Beellama Mirkaneessi",
        ["appt.cancel"] = "Beellama Haqi",
        ["appt.reschedule"] = "Beellama Jijjiiri",
        ["appt.status.scheduled"] = "Qabameera",
        ["appt.status.completed"] = "Xumurameera",
        ["appt.status.cancelled"] = "Haqameera",
        ["appt.dualCalendarNotice"] = "Kalaandara Itoophiyaa fi Faranjiitiin mul'ata.",

        // Billing & Payments
        ["billing.statements"] = "Tarree Kaffaltii",
        ["billing.amountDue"] = "Hanga Kaffalamu",
        ["billing.payNow"] = "Amma Kaffali",
        ["billing.paymentPlan"] = "Karoora Kaffaltii",
        ["billing.receipt"] = "Nagahee",
        ["billing.status.paid"] = "Kaffalameera",
        ["billing.status.pending"] = "Eeggachaa jira",
        ["billing.status.overdue"] = "Yeroon darbeera",

        // Messages & Notifications
        ["msg.inbox"] = "Saanduqa Ergaa",
        ["msg.compose"] = "Ergaa Haaraa Barreessi",
        ["msg.send"] = "Ergi",
        ["msg.recipient"] = "Fudhataa",
        ["msg.subject"] = "Dhimma",
        ["msg.body"] = "Qabiyyee Ergaa",

        // Emergency Break-Glass
        ["breakglass.title"] = "Hayyama Yeroo Balaa Qorannoo",
        ["breakglass.warning"] = "Akeekkachiisa: Hayyamni kun cimsamee hordofama. Haala balaa lubbuu oolchuuf qofa fayyadamaa.",
        ["breakglass.justification"] = "Sababa Yaalaa",
        ["breakglass.activate"] = "Hayyama Eegali",
        ["breakglass.conclude"] = "Hayyama Xumuri",

        // Ethiopian Calendar
        ["cal.enkutatash"] = "Ayyaana Bara Haaraa (Inquxaaxash)",
        ["cal.pagume"] = "Qaammee (Ji'a 13ffaa)",
        ["cal.fiscalYear"] = "Bara Baajata Itoophiyaa",
        ["cal.quarter"] = "Kurmaana Baajataa"
    };

    public static IReadOnlyDictionary<string, string> GetDictionary(string lang)
    {
        return (lang?.ToLowerInvariant()) switch
        {
            "am" => Amharic,
            "om" => Oromo,
            _ => English
        };
    }
}
