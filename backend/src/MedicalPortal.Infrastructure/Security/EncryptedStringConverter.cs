using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MedicalPortal.Infrastructure.Security;

public static class FieldEncryptionAccessor
{
    private static IFieldEncryptionService? _current;
    private static readonly object _lock = new();

    public static IFieldEncryptionService Current
    {
        get
        {
            if (_current == null)
            {
                lock (_lock)
                {
                    if (_current == null)
                    {
                        var key = new byte[32];
                        RandomNumberGenerator.Fill(key);
                        var keyProvider = new ConfigurationKeyProvider(
                            new Dictionary<string, byte[]> { { "v1", key } }, "v1");
                        _current = new AesGcmFieldEncryptionService(keyProvider);
                    }
                }
            }
            return _current;
        }
    }

    public static void Initialize(IFieldEncryptionService service)
    {
        _current = service ?? throw new ArgumentNullException(nameof(service));
    }

    public static string? Encrypt(string? value) => Current.Encrypt(value);
    public static string? Decrypt(string? value) => Current.Decrypt(value);
}

public class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter()
        : base(
            v => string.IsNullOrEmpty(v) ? v : FieldEncryptionAccessor.Encrypt(v),
            v => string.IsNullOrEmpty(v) ? v : FieldEncryptionAccessor.Decrypt(v))
    {
    }
}
