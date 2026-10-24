using System.Text.RegularExpressions;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Notifications;

public class NotificationDeliveryService : INotificationDeliveryService
{
    private readonly MedicalPortalDbContext _db;
    private readonly INotificationSender _sender;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationDeliveryService> _logger;

    public NotificationDeliveryService(
        MedicalPortalDbContext db,
        INotificationSender sender,
        IConfiguration config,
        ILogger<NotificationDeliveryService>? logger = null)
    {
        _db = db;
        _sender = sender;
        _config = config;
        _logger = logger ?? NullLogger<NotificationDeliveryService>.Instance;
    }

    private string GetPortalBaseUrl()
    {
        var url = _config["Notifications:PortalBaseUrl"] ?? _config["PORTAL_BASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
        {
            url = "https://portal.springfieldhealth.org";
        }
        return url.TrimEnd('/');
    }

    private int GetMaxRetryAttempts()
    {
        return _config.GetValue<int>("Notifications:MaxRetryAttempts", 3);
    }

    private int GetInitialBackoffMs()
    {
        return _config.GetValue<int>("Notifications:InitialBackoffMs", 100);
    }

    public async Task<List<NotificationDeliveryRecord>> SendAppointmentReminderAsync(
        Appointment appointment,
        CancellationToken cancellationToken = default)
    {
        var records = new List<NotificationDeliveryRecord>();

        // Ensure patient and person are loaded
        var patient = appointment.Patient ?? await _db.Patients
            .Include(p => p.Person)
            .FirstOrDefaultAsync(p => p.Id == appointment.PatientId, cancellationToken);

        if (patient?.Person == null)
        {
            _logger.LogWarning("Cannot send appointment reminder for appointment {AppointmentId}: Patient or Person not found.", appointment.Id);
            return records;
        }

        var person = patient.Person;
        var portalUrl = $"{GetPortalBaseUrl()}/appointments";

        // Generic non-clinical reminder linking to portal (HIPAA compliance)
        var dateFormatted = appointment.StartAt.ToString("dddd, MMMM d, yyyy 'at' h:mm tt 'UTC'");
        var emailSubject = "Springfield Health System - Appointment Reminder";
        var emailBody = $"Hello {person.FirstName},\n\nYou have an upcoming appointment scheduled on {dateFormatted}.\n\nTo view your appointment details, complete pre-visit check-in, or get directions, please sign in to your secure patient portal:\n{portalUrl}\n\nThank you,\nSpringfield Health System";

        var smsDateFormatted = appointment.StartAt.ToString("MMM d 'at' h:mm tt 'UTC'");
        var smsMessage = $"Springfield Health System: You have an upcoming appointment on {smsDateFormatted}. Sign in to your patient portal for details: {portalUrl}";

        // Scrub to guarantee no clinical details
        AssertNoClinicalDetails(emailBody, appointment.ReasonForVisit, appointment.PatientNotes);
        AssertNoClinicalDetails(smsMessage, appointment.ReasonForVisit, appointment.PatientNotes);

        var refType = "Appointment";
        var refId = appointment.Id.ToString();

        // Check preferences
        var prefs = await GetPreferencesAsync(person.Id, "Appointments", cancellationToken);

        if (prefs.Muted)
        {
            records.Add(await CreateOptedOutRecordAsync(person, NotificationChannel.EMAIL, "Appointments", emailSubject, emailBody, refType, refId, "Muted by recipient preference", cancellationToken));
            return records;
        }

        if (prefs.EmailEnabled)
        {
            var emailRecord = await DispatchEmailInternalAsync(person, "Appointments", emailSubject, emailBody, refType, refId, cancellationToken);
            records.Add(emailRecord);
        }

        if (prefs.SmsEnabled)
        {
            var smsRecord = await DispatchSmsInternalAsync(person, "Appointments", smsMessage, refType, refId, cancellationToken);
            records.Add(smsRecord);
        }

        return records;
    }

    public async Task<List<NotificationDeliveryRecord>> SendBillingNoticeAsync(
        Person person,
        Guid patientId,
        int amountCents,
        DateTime? dueAt,
        string referenceId,
        CancellationToken cancellationToken = default)
    {
        var records = new List<NotificationDeliveryRecord>();
        var portalUrl = $"{GetPortalBaseUrl()}/billing";
        var amountStr = $"${(amountCents / 100.0):F2}";
        var dueStr = dueAt.HasValue ? dueAt.Value.ToString("MMMM d, yyyy") : "immediately";

        var emailSubject = "Springfield Health System - Billing Notice";
        var emailBody = $"Hello {person.FirstName},\n\nYou have an overdue payment installment or balance of {amountStr} due on {dueStr}.\n\nTo view your statement details, update payment options, or make a secure payment, please sign in to your patient portal:\n{portalUrl}\n\nThank you,\nSpringfield Health System";

        var smsMessage = $"Springfield Health System: You have a payment installment of {amountStr} due {dueStr}. Review and pay securely via the patient portal: {portalUrl}";

        var refType = "PaymentInstallment";
        var refId = referenceId;

        var prefs = await GetPreferencesAsync(person.Id, "Billing", cancellationToken);

        if (prefs.Muted)
        {
            records.Add(await CreateOptedOutRecordAsync(person, NotificationChannel.EMAIL, "Billing", emailSubject, emailBody, refType, refId, "Muted by recipient preference", cancellationToken));
            return records;
        }

        if (prefs.EmailEnabled)
        {
            var emailRecord = await DispatchEmailInternalAsync(person, "Billing", emailSubject, emailBody, refType, refId, cancellationToken);
            records.Add(emailRecord);
        }

        if (prefs.SmsEnabled)
        {
            var smsRecord = await DispatchSmsInternalAsync(person, "Billing", smsMessage, refType, refId, cancellationToken);
            records.Add(smsRecord);
        }

        return records;
    }

    public async Task<List<NotificationDeliveryRecord>> SendPaymentReceiptAsync(
        Person person,
        Guid patientId,
        int amountCents,
        string referenceId,
        CancellationToken cancellationToken = default)
    {
        var records = new List<NotificationDeliveryRecord>();
        var portalUrl = $"{GetPortalBaseUrl()}/billing";
        var amountStr = $"${(amountCents / 100.0):F2}";

        var emailSubject = "Springfield Health System - Payment Confirmation";
        var emailBody = $"Hello {person.FirstName},\n\nWe received your payment of {amountStr}. Thank you.\n\nTo view your updated balance and payment history, please sign in to your patient portal:\n{portalUrl}\n\nSpringfield Health System";

        var smsMessage = $"Springfield Health System: We received your payment of {amountStr}. View your receipt in the patient portal: {portalUrl}";

        var refType = "Payment";
        var refId = referenceId;

        var prefs = await GetPreferencesAsync(person.Id, "Billing", cancellationToken);

        if (prefs.Muted)
        {
            records.Add(await CreateOptedOutRecordAsync(person, NotificationChannel.EMAIL, "Billing", emailSubject, emailBody, refType, refId, "Muted by recipient preference", cancellationToken));
            return records;
        }

        if (prefs.EmailEnabled)
        {
            var emailRecord = await DispatchEmailInternalAsync(person, "Billing", emailSubject, emailBody, refType, refId, cancellationToken);
            records.Add(emailRecord);
        }

        if (prefs.SmsEnabled)
        {
            var smsRecord = await DispatchSmsInternalAsync(person, "Billing", smsMessage, refType, refId, cancellationToken);
            records.Add(smsRecord);
        }

        return records;
    }

    public Task<NotificationDeliveryRecord> DispatchEmailAsync(
        Person person,
        string category,
        string subject,
        string body,
        string? referenceType = null,
        string? referenceId = null,
        CancellationToken cancellationToken = default)
    {
        return DispatchEmailInternalAsync(person, category, subject, body, referenceType, referenceId, cancellationToken);
    }

    public Task<NotificationDeliveryRecord> DispatchSmsAsync(
        Person person,
        string category,
        string message,
        string? referenceType = null,
        string? referenceId = null,
        CancellationToken cancellationToken = default)
    {
        return DispatchSmsInternalAsync(person, category, message, referenceType, referenceId, cancellationToken);
    }

    private async Task<NotificationDeliveryRecord> DispatchEmailInternalAsync(
        Person person,
        string category,
        string subject,
        string body,
        string? referenceType,
        string? referenceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(person.Email))
        {
            return await CreateOptedOutRecordAsync(person, NotificationChannel.EMAIL, category, subject, body, referenceType, referenceId, "No email address on file", cancellationToken);
        }

        var record = new NotificationDeliveryRecord
        {
            RecipientPersonId = person.Id,
            Channel = NotificationChannel.EMAIL,
            Category = category,
            RecipientAddress = person.Email,
            Subject = subject,
            Body = body,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Status = NotificationDeliveryStatus.QUEUED,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.NotificationDeliveryRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        // Perform delivery with retry and exponential backoff
        var maxAttempts = GetMaxRetryAttempts();
        var initialBackoff = GetInitialBackoffMs();
        int attempt = 0;
        NotificationSendResult? result = null;

        while (attempt < maxAttempts)
        {
            attempt++;
            record.AttemptCount = attempt;

            var req = new NotificationEmailRequest(person.Email, subject, body, Category: category);
            result = await _sender.SendEmailAsync(req, cancellationToken);

            if (result.Success)
            {
                record.Status = NotificationDeliveryStatus.DELIVERED;
                record.SentAtUtc = DateTime.UtcNow;
                record.DeliveredAtUtc = DateTime.UtcNow;
                record.ExternalMessageId = result.ExternalMessageId;
                record.ErrorMessage = null;
                break;
            }

            record.ErrorMessage = result.ErrorMessage;

            if (!result.IsTransientError || attempt >= maxAttempts)
            {
                record.Status = NotificationDeliveryStatus.FAILED;
                break;
            }

            // Exponential backoff
            var delayMs = initialBackoff * (int)Math.Pow(2, attempt - 1);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        if (result != null && !result.Success)
        {
            record.Status = NotificationDeliveryStatus.FAILED;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    private async Task<NotificationDeliveryRecord> DispatchSmsInternalAsync(
        Person person,
        string category,
        string message,
        string? referenceType,
        string? referenceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(person.Phone))
        {
            return await CreateOptedOutRecordAsync(person, NotificationChannel.SMS, category, string.Empty, message, referenceType, referenceId, "No phone number on file", cancellationToken);
        }

        var record = new NotificationDeliveryRecord
        {
            RecipientPersonId = person.Id,
            Channel = NotificationChannel.SMS,
            Category = category,
            RecipientAddress = person.Phone,
            Subject = string.Empty,
            Body = message,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Status = NotificationDeliveryStatus.QUEUED,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.NotificationDeliveryRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        // Perform delivery with retry and exponential backoff
        var maxAttempts = GetMaxRetryAttempts();
        var initialBackoff = GetInitialBackoffMs();
        int attempt = 0;
        NotificationSendResult? result = null;

        while (attempt < maxAttempts)
        {
            attempt++;
            record.AttemptCount = attempt;

            var req = new NotificationSmsRequest(person.Phone, message, Category: category);
            result = await _sender.SendSmsAsync(req, cancellationToken);

            if (result.Success)
            {
                record.Status = NotificationDeliveryStatus.DELIVERED;
                record.SentAtUtc = DateTime.UtcNow;
                record.DeliveredAtUtc = DateTime.UtcNow;
                record.ExternalMessageId = result.ExternalMessageId;
                record.ErrorMessage = null;
                break;
            }

            record.ErrorMessage = result.ErrorMessage;

            if (!result.IsTransientError || attempt >= maxAttempts)
            {
                record.Status = NotificationDeliveryStatus.FAILED;
                break;
            }

            // Exponential backoff
            var delayMs = initialBackoff * (int)Math.Pow(2, attempt - 1);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        if (result != null && !result.Success)
        {
            record.Status = NotificationDeliveryStatus.FAILED;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    private async Task<NotificationDeliveryRecord> CreateOptedOutRecordAsync(
        Person person,
        NotificationChannel channel,
        string category,
        string subject,
        string body,
        string? referenceType,
        string? referenceId,
        string reason,
        CancellationToken cancellationToken)
    {
        var record = new NotificationDeliveryRecord
        {
            RecipientPersonId = person.Id,
            Channel = channel,
            Category = category,
            RecipientAddress = channel == NotificationChannel.EMAIL ? (person.Email ?? string.Empty) : (person.Phone ?? string.Empty),
            Subject = subject,
            Body = body,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Status = NotificationDeliveryStatus.OPTED_OUT,
            ErrorMessage = reason,
            AttemptCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.NotificationDeliveryRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    private async Task<(bool Muted, bool EmailEnabled, bool SmsEnabled)> GetPreferencesAsync(
        Guid personId,
        string category,
        CancellationToken cancellationToken)
    {
        var pref = await _db.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PersonId == personId && p.Category == category, cancellationToken);

        if (pref == null)
        {
            // Default settings when no preference configured
            return (Muted: false, EmailEnabled: true, SmsEnabled: true);
        }

        return (Muted: pref.Muted, EmailEnabled: pref.EmailEnabled, SmsEnabled: pref.SmsEnabled);
    }

    private static void AssertNoClinicalDetails(string message, params string?[] clinicalDetails)
    {
        foreach (var detail in clinicalDetails)
        {
            if (!string.IsNullOrWhiteSpace(detail) && detail.Length > 2)
            {
                if (message.Contains(detail, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"PHI hygiene violation: notification message contains clinical detail '{detail}'. Notifications must only link to the patient portal.");
                }
            }
        }
    }
}
