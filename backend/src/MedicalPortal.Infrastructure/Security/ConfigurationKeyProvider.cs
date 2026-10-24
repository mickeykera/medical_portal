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

        // If no keys configured, check environment
        if (!_keys.ContainsKey(_activeKeyVersion))
        {
            var env = configuration["ASPNETCORE_ENVIRONMENT"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var isProduction = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);

            if (isProduction)
            {
                throw new InvalidOperationException(
                    $"Production startup failed: FieldEncryption key for active version '{_activeKeyVersion}' is not configured. " +
                    "Sensitive data encryption requires an explicit key (e.g. FieldEncryption:Keys:{version} or FIELD_ENCRYPTION_KEY_{VERSION}). " +
                    "Deriving encryption keys from authentication secrets (JWT_SECRET) is strictly prohibited.");
            }

            // In non-production/test environments without explicit keys, use a consistent 256-bit development master key
            // (independent of JWT_SECRET, ensuring deterministic test runs and cross-test database consistency)
            using var sha = SHA256.Create();
            _keys[_activeKeyVersion] = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("MedicalPortal_Dev_FieldEncryption_MasterKey_v1"));
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
