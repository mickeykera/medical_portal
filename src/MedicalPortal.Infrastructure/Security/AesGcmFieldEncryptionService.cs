using System.Security.Cryptography;
using System.Text;

namespace MedicalPortal.Infrastructure.Security;

public class AesGcmFieldEncryptionService : IFieldEncryptionService
{
    private const string EnvelopePrefix = "enc:";
    private const int NonceByteLength = 12; // 96 bits recommended for AES-GCM
    private const int TagByteLength = 16;   // 128 bits authentication tag

    private readonly IKeyProvider _keyProvider;

    public AesGcmFieldEncryptionService(IKeyProvider keyProvider)
    {
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
    }

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        var keyVersion = _keyProvider.ActiveKeyVersion;
        var key = _keyProvider.GetKey(keyVersion);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var nonce = new byte[NonceByteLength];
        var tag = new byte[TagByteLength];

        RandomNumberGenerator.Fill(nonce);

        using (var aesGcm = new AesGcm(key, TagByteLength))
        {
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var nonceB64 = Convert.ToBase64String(nonce);
        var tagB64 = Convert.ToBase64String(tag);
        var cipherB64 = Convert.ToBase64String(cipherBytes);

        return $"{EnvelopePrefix}{keyVersion}:{nonceB64}:{tagB64}:{cipherB64}";
    }

    public string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        if (!IsEncrypted(cipherText))
        {
            // Unencrypted legacy string or plaintext; pass through safely
            return cipherText;
        }

        var parts = cipherText.Split(':');
        if (parts.Length != 5 || parts[0] != "enc")
        {
            throw new CryptographicException("Malformed encryption envelope format.");
        }

        var keyVersion = parts[1];
        var nonce = Convert.FromBase64String(parts[2]);
        var tag = Convert.FromBase64String(parts[3]);
        var cipherBytes = Convert.FromBase64String(parts[4]);

        var key = _keyProvider.GetKey(keyVersion);
        var plainBytes = new byte[cipherBytes.Length];

        try
        {
            using (var aesGcm = new AesGcm(key, TagByteLength))
            {
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Authentication tag mismatch or corrupted ciphertext.");
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    public bool IsEncrypted(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!text.StartsWith(EnvelopePrefix, StringComparison.Ordinal)) return false;

        var parts = text.Split(':');
        return parts.Length == 5 && parts[0] == "enc";
    }

    public string? RotateKey(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        if (!IsEncrypted(cipherText))
        {
            return Encrypt(cipherText);
        }

        var parts = cipherText.Split(':');
        var keyVersion = parts[1];

        // If already encrypted with active key version, nothing to do
        if (string.Equals(keyVersion, _keyProvider.ActiveKeyVersion, StringComparison.OrdinalIgnoreCase))
        {
            return cipherText;
        }

        var plainText = Decrypt(cipherText);
        return Encrypt(plainText);
    }
}
