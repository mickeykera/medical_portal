using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Logging;
using MedicalPortal.Infrastructure.Security;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MedicalPortal.Tests;

public class PhiHygieneTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PhiHygieneTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void PhiScrubber_RedactsSsn_InText()
    {
        var scrubber = new PhiScrubber();
        var raw = "Patient SSN is 123-45-6789 and secondary is 987 65 4321.";
        var scrubbed = scrubber.ScrubText(raw);

        Assert.DoesNotContain("123-45-6789", scrubbed);
        Assert.DoesNotContain("987 65 4321", scrubbed);
        Assert.Contains("[SSN-REDACTED]", scrubbed);
    }

    [Fact]
    public void PhiScrubber_RedactsDatesOfBirth_InIsoAndUsFormat()
    {
        var scrubber = new PhiScrubber();
        var rawIso = "Patient date of birth: 1984-07-23 recorded in system.";
        var scrubbedIso = scrubber.ScrubText(rawIso);

        Assert.DoesNotContain("1984-07-23", scrubbedIso);
        Assert.Contains("[DOB-REDACTED]", scrubbedIso);

        var rawUs = "Patient DOB: 07/23/1984 confirmed on chart.";
        var scrubbedUs = scrubber.ScrubText(rawUs);

        Assert.DoesNotContain("07/23/1984", scrubbedUs);
        Assert.Contains("[DOB-REDACTED]", scrubbedUs);
    }

    [Fact]
    public void PhiScrubber_RedactsMrn_InVariousFormats()
    {
        var scrubber = new PhiScrubber();
        var raw = "Record belongs to MRN-884920 and mrn: AB9912 in facility.";
        var scrubbed = scrubber.ScrubText(raw);

        Assert.DoesNotContain("MRN-884920", scrubbed);
        Assert.DoesNotContain("AB9912", scrubbed);
        Assert.Contains("[MRN-REDACTED]", scrubbed);
    }

    [Fact]
    public void PhiScrubber_RedactsEmailsAndPhones()
    {
        var scrubber = new PhiScrubber();
        var raw = "Contact patient at patient.doe@example.org or by phone at (555) 234-5678 or 555-876-5432.";
        var scrubbed = scrubber.ScrubText(raw);

        Assert.DoesNotContain("patient.doe@example.org", scrubbed);
        Assert.DoesNotContain("(555) 234-5678", scrubbed);
        Assert.DoesNotContain("555-876-5432", scrubbed);
        Assert.Contains("[EMAIL-REDACTED]", scrubbed);
        Assert.Contains("[PHONE-REDACTED]", scrubbed);
    }

    [Fact]
    public void PhiScrubber_LeavesBenignSystemMessages_Untouched()
    {
        var scrubber = new PhiScrubber();
        var raw = "User authenticated. Target patient ID: 3e8b09b5-4b1f-4b07-9b2f-9876543210ab. Execution took 25ms. Status: 200 OK.";
        var scrubbed = scrubber.ScrubText(raw);

        Assert.Equal(raw, scrubbed);
    }

    [Fact]
    public void PhiScrubber_ScrubProperties_RedactsSensitiveKeys()
    {
        var scrubber = new PhiScrubber();
        var props = new Dictionary<string, object?>
        {
            { "patientNotes", "Patient experienced severe chest pain after meal." },
            { "mrn", "MRN-12345" },
            { "addressLine1", "742 Evergreen Terrace" },
            { "requestId", "req-12345" },
            { "status", "SUCCESS" }
        };

        var scrubbed = scrubber.ScrubProperties(props);

        Assert.Equal("[REDACTED]", scrubbed["patientNotes"]);
        Assert.Equal("[REDACTED]", scrubbed["mrn"]);
        Assert.Equal("[REDACTED]", scrubbed["addressLine1"]);
        Assert.Equal("req-12345", scrubbed["requestId"]);
        Assert.Equal("SUCCESS", scrubbed["status"]);
    }

    [Fact]
    public void PhiScrubber_ScrubException_SanitizesMessageAndStackTrace()
    {
        var scrubber = new PhiScrubber();
        var ex = new InvalidOperationException("Failed processing patient with SSN 123-45-6789 and MRN-998822 on 1980-01-15.");
        var sanitized = scrubber.ScrubException(ex);

        Assert.NotNull(sanitized);
        Assert.DoesNotContain("123-45-6789", sanitized.Message);
        Assert.DoesNotContain("MRN-998822", sanitized.Message);
        Assert.DoesNotContain("1980-01-15", sanitized.Message);
        Assert.Contains("[SSN-REDACTED]", sanitized.Message);
        Assert.Contains("[MRN-REDACTED]", sanitized.Message);
        Assert.Contains("[DOB-REDACTED]", sanitized.Message);
    }

    [Fact]
    public void ScrubbingLogger_ScrubsStructuredLogArguments()
    {
        var scrubber = new PhiScrubber();
        string? loggedOutput = null;

        var provider = new ScrubbingLoggerProvider(scrubber, line => loggedOutput = line);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("Patient note: {Notes} for patient with MRN: {MRN}", "Severe migraine", "MRN-998811");

        Assert.NotNull(loggedOutput);
        Assert.DoesNotContain("MRN-998811", loggedOutput);
        Assert.Contains("[MRN-REDACTED]", loggedOutput);
    }

    [Fact]
    public void FieldEncryptionService_EncryptAndDecrypt_RoundtripSuccess()
    {
        var keyProvider = new ConfigurationKeyProvider(
            new Dictionary<string, byte[]>
            {
                { "v1", RandomNumberGenerator.GetBytes(32) }
            },
            "v1");

        var encService = new AesGcmFieldEncryptionService(keyProvider);

        var originalNotes = "Patient diagnosed with Type 2 Diabetes; fasting glucose 185 mg/dL. Prescribed Metformin 500mg BID.";
        var encrypted = encService.Encrypt(originalNotes);

        Assert.NotNull(encrypted);
        Assert.NotEqual(originalNotes, encrypted);
        Assert.True(encService.IsEncrypted(encrypted));
        Assert.StartsWith("enc:v1:", encrypted);

        var decrypted = encService.Decrypt(encrypted);
        Assert.Equal(originalNotes, decrypted);
    }

    [Fact]
    public void FieldEncryptionService_AddressEncryption_RoundtripSuccess()
    {
        var keyProvider = new ConfigurationKeyProvider(
            new Dictionary<string, byte[]>
            {
                { "v1", RandomNumberGenerator.GetBytes(32) }
            },
            "v1");

        var encService = new AesGcmFieldEncryptionService(keyProvider);

        var address = "456 Oak Street, Apt 7B";
        var encrypted = encService.Encrypt(address);
        var decrypted = encService.Decrypt(encrypted);

        Assert.True(encService.IsEncrypted(encrypted));
        Assert.Equal(address, decrypted);
    }

    [Fact]
    public void FieldEncryptionService_UnencryptedText_PassesThroughSafely()
    {
        var keyProvider = new ConfigurationKeyProvider(
            new Dictionary<string, byte[]>
            {
                { "v1", RandomNumberGenerator.GetBytes(32) }
            },
            "v1");

        var encService = new AesGcmFieldEncryptionService(keyProvider);

        var legacyText = "Legacy unencrypted notes in database prior to migration.";
        Assert.False(encService.IsEncrypted(legacyText));

        var decrypted = encService.Decrypt(legacyText);
        Assert.Equal(legacyText, decrypted);
    }

    [Fact]
    public void FieldEncryptionService_TamperedCiphertext_ThrowsCryptographicException()
    {
        var keyProvider = new ConfigurationKeyProvider(
            new Dictionary<string, byte[]>
            {
                { "v1", RandomNumberGenerator.GetBytes(32) }
            },
            "v1");

        var encService = new AesGcmFieldEncryptionService(keyProvider);

        var original = "Confidential clinical free text";
        var encrypted = encService.Encrypt(original)!;

        // Tamper with the ciphertext envelope
        var parts = encrypted.Split(':');
        var cipherBytes = Convert.FromBase64String(parts[4]);
        cipherBytes[0] ^= 0xFF; // Flip bits
        var tamperedCipher = $"{parts[0]}:{parts[1]}:{parts[2]}:{parts[3]}:{Convert.ToBase64String(cipherBytes)}";

        Assert.Throws<CryptographicException>(() => encService.Decrypt(tamperedCipher));
    }

    [Fact]
    public void FieldEncryptionService_KeyRotation_ReEncryptsWithNewKeyVersion()
    {
        var keyV1 = RandomNumberGenerator.GetBytes(32);
        var keyV2 = RandomNumberGenerator.GetBytes(32);

        // Initially v1 is active
        var keyProvider = new ConfigurationKeyProvider(
            new Dictionary<string, byte[]>
            {
                { "v1", keyV1 },
                { "v2", keyV2 }
            },
            "v1");

        var encServiceV1 = new AesGcmFieldEncryptionService(keyProvider);

        var originalText = "Sensitive clinical notes to be rotated.";
        var encryptedV1 = encServiceV1.Encrypt(originalText);

        Assert.StartsWith("enc:v1:", encryptedV1);

        // Now simulate rotating active key to v2
        var rotatedKeyProvider = new ConfigurationKeyProvider(
            new Dictionary<string, byte[]>
            {
                { "v1", keyV1 },
                { "v2", keyV2 }
            },
            "v2");

        var encServiceV2 = new AesGcmFieldEncryptionService(rotatedKeyProvider);

        // Can still decrypt v1 payload
        var decryptedBeforeRotation = encServiceV2.Decrypt(encryptedV1);
        Assert.Equal(originalText, decryptedBeforeRotation);

        // Rotate key for this record
        var rotatedEnvelope = encServiceV2.RotateKey(encryptedV1);

        Assert.NotNull(rotatedEnvelope);
        Assert.StartsWith("enc:v2:", rotatedEnvelope);
        Assert.NotEqual(encryptedV1, rotatedEnvelope);

        // Can decrypt rotated envelope with v2
        var decryptedAfterRotation = encServiceV2.Decrypt(rotatedEnvelope);
        Assert.Equal(originalText, decryptedAfterRotation);
    }

    [Fact]
    public async Task ExceptionHandler_UnhandledExceptionWithPhi_ReturnsSanitizedResponseWithoutPhi()
    {
        var scrubber = new PhiScrubber();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddPhiScrubber());
        var logger = loggerFactory.CreateLogger<PhiSafeExceptionHandlerMiddleware>();

        var middleware = new PhiSafeExceptionHandlerMiddleware(
            async context =>
            {
                await Task.Yield();
                // Simulate exception containing patient PHI and clinical values
                throw new InvalidOperationException("Patient John Doe with SSN 123-45-6789 and MRN-778899 glucose 450 mg/dL crashed database query.");
            },
            logger,
            scrubber);

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        Assert.NotEmpty(responseBody);

        // Verify response NEVER leaks the raw exception message or patient details
        Assert.DoesNotContain("John Doe", responseBody);
        Assert.DoesNotContain("123-45-6789", responseBody);
        Assert.DoesNotContain("MRN-778899", responseBody);
        Assert.DoesNotContain("glucose 450", responseBody);
        Assert.DoesNotContain("crashed database query", responseBody);

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("error", out var errorProp));
        Assert.Equal("An error occurred while processing your request.", errorProp.GetString());
        Assert.True(root.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task AppointmentReminderWorker_LogsPatientId_WithoutPatientName()
    {
        var scrubber = new PhiScrubber();
        string? loggedOutput = null;

        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new ScrubbingLoggerProvider(scrubber, line => loggedOutput = line)));
        var workerLogger = loggerFactory.CreateLogger<AppointmentReminderWorker>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var patient = await db.Patients.Include(p => p.Person).FirstAsync();
        var provider = await db.Providers.FirstAsync();

        var appt = new Appointment
        {
            PatientId = patient.Id,
            ProviderId = provider.Id,
            StartAt = DateTime.UtcNow.AddHours(25),
            EndAt = DateTime.UtcNow.AddHours(25.5),
            Status = AppointmentStatus.CONFIRMED,
            ReasonForVisit = "Follow up test",
            PatientNotes = "Confidential note"
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        var worker = new AppointmentReminderWorker(scope.ServiceProvider, workerLogger);
        var count = await worker.ProcessUpcomingAppointmentRemindersAsync(CancellationToken.None);

        Assert.True(count >= 1);
        Assert.NotNull(loggedOutput);

        // Verify patient full name is not in the log output, but PatientId is
        var fullName = $"{patient.Person.FirstName} {patient.Person.LastName}";
        Assert.DoesNotContain(fullName, loggedOutput);
        Assert.Contains(patient.Id.ToString(), loggedOutput);
    }
}
