namespace MedicalPortal.Infrastructure.Notifications;

public class FakeNotificationSender : INotificationSender
{
    private readonly object _lock = new();

    public List<NotificationEmailRequest> SentEmails { get; } = new();
    public List<NotificationSmsRequest> SentSms { get; } = new();

    public int FailEmailAttemptsRemaining { get; set; } = 0;
    public int FailSmsAttemptsRemaining { get; set; } = 0;
    public bool EmailFailIsTransient { get; set; } = true;
    public bool SmsFailIsTransient { get; set; } = true;
    public string SimulatedErrorMessage { get; set; } = "Simulated delivery provider error.";

    public Task<NotificationSendResult> SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (FailEmailAttemptsRemaining > 0)
            {
                FailEmailAttemptsRemaining--;
                return Task.FromResult(new NotificationSendResult(
                    Success: false,
                    ErrorMessage: SimulatedErrorMessage,
                    IsTransientError: EmailFailIsTransient));
            }

            SentEmails.Add(request);
            var id = $"fake_email_{Guid.NewGuid():N}";
            return Task.FromResult(new NotificationSendResult(Success: true, ExternalMessageId: id));
        }
    }

    public Task<NotificationSendResult> SendSmsAsync(NotificationSmsRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (FailSmsAttemptsRemaining > 0)
            {
                FailSmsAttemptsRemaining--;
                return Task.FromResult(new NotificationSendResult(
                    Success: false,
                    ErrorMessage: SimulatedErrorMessage,
                    IsTransientError: SmsFailIsTransient));
            }

            SentSms.Add(request);
            var id = $"fake_sms_{Guid.NewGuid():N}";
            return Task.FromResult(new NotificationSendResult(Success: true, ExternalMessageId: id));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            SentEmails.Clear();
            SentSms.Clear();
            FailEmailAttemptsRemaining = 0;
            FailSmsAttemptsRemaining = 0;
            EmailFailIsTransient = true;
            SmsFailIsTransient = true;
            SimulatedErrorMessage = "Simulated delivery provider error.";
        }
    }
}
