using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace MedicalPortal.Infrastructure.Security;

public class ConfigurationKeyProvider : IKeyProvider
{
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _activeKeyVersion;

    public ConfigurationKeyProvider(IConfiguration configuration)
    {
        _activeKeyVersion = configuration["FieldEncryption:ActiveKeyVersion"]
            ?? configuration["FIELD_ENCRYPTION_ACTIVE_KEY_VERSION"]
            ?? "v1";

        // Read keys from FieldEncryption:Keys section or environment variables
        var section = configuration.GetSection("FieldEncryption:Keys");
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Key) && !string.IsNullOrWhiteSpace(child.Value))
            {
                _keys[child.Key] = ParseKeyBytes(child.Value);
            }
        }

        // Check direct env vars like FIELD_ENCRYPTION_KEY_V1
        if (configuration is IConfigurationRoot root)
        {
            foreach (var kvp in configuration.AsEnumerable())
            {
                if (kvp.Key.StartsWith("FIELD_ENCRYPTION_KEY_", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    var version = kvp.Key["FIELD_ENCRYPTION_KEY_".Length..].ToLowerInvariant();
                    if (!_keys.ContainsKey(version))
                    {
                        _keys[version] = ParseKeyBytes(kvp.Value);
                    }
                }
            }
        }

        // If no keys configured (e.g. fresh dev environment), generate a persistent in-memory 256-bit key for v1
        if (!_keys.ContainsKey(_activeKeyVersion))
        {
            // Use deterministic fallback derived from JWT secret or machine seed if available, or generate 32 random bytes
            var fallbackSeed = configuration["Jwt:Secret"] ?? configuration["JWT_SECRET"];
            if (!string.IsNullOrWhiteSpace(fallbackSeed))
            {
                using var sha = SHA256.Create();
                _keys[_activeKeyVersion] = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fallbackSeed + "_FieldEncKey_v1"));
            }
            else
            {
                var randomKey = new byte[32];
                RandomNumberGenerator.Fill(randomKey);
                _keys[_activeKeyVersion] = randomKey;
            }
        }
    }

    public ConfigurationKeyProvider(IDictionary<string, byte[]> keys, string activeKeyVersion)
    {
        if (keys == null || keys.Count == 0)
            throw new ArgumentException("Keys dictionary must contain at least one key.", nameof(keys));

        foreach (var kvp in keys)
        {
            if (kvp.Value.Length != 32)
                throw new ArgumentException($"Key '{kvp.Key}' must be 32 bytes (256 bits). Found {kvp.Value.Length} bytes.");

            _keys[kvp.Key] = kvp.Value;
        }

        _activeKeyVersion = activeKeyVersion;
        if (!_keys.ContainsKey(_activeKeyVersion))
            throw new ArgumentException($"Active key version '{_activeKeyVersion}' is not present in provided keys.", nameof(activeKeyVersion));
    }

    public string ActiveKeyVersion => _activeKeyVersion;

    public IEnumerable<string> AvailableKeyVersions => _keys.Keys;

    public bool HasKey(string keyVersion) => _keys.ContainsKey(keyVersion);

    public byte[] GetKey(string keyVersion)
    {
        if (_keys.TryGetValue(keyVersion, out var key))
        {
            return key;
        }
        throw new KeyNotFoundException($"Field encryption key for version '{keyVersion}' was not found.");
    }

    private static byte[] ParseKeyBytes(string keyStr)
    {
        var trimmed = keyStr.Trim();
        // Check if Hex encoded (64 hex characters for 32 bytes)
        if (trimmed.Length == 64 && IsHexString(trimmed))
        {
            return Convert.FromHexString(trimmed);
        }

        // Try Base64
        try
        {
            var bytes = Convert.FromBase64String(trimmed);
            if (bytes.Length == 32) return bytes;
        }
        catch
        {
            // fallback
        }

        // Fallback: SHA-256 hash string representation to guarantee exactly 32 bytes
        using var sha = SHA256.Create();
        return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(trimmed));
    }

    private static bool IsHexString(string input)
    {
        foreach (char c in input)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }
}
