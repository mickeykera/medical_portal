using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MedicalPortal.Infrastructure.Tenancy;

public interface ITenantKeyProvider
{
    byte[] GetTenantKey(Guid tenantId, string? keyVersion = null);
    string GetActiveTenantKeyVersion(Guid tenantId);
}

public class TenantKeyProvider : ITenantKeyProvider
{
    private readonly byte[] _masterKey;
    private readonly string _activeVersion;

    public TenantKeyProvider(IConfiguration config)
    {
        var rawMaster = config["FieldEncryption:MasterKey"]
            ?? config["FIELD_ENCRYPTION_MASTER_KEY"]
            ?? config["JWT_SECRET"]
            ?? "MedicalPortalMasterKeySecure20261024!";

        _masterKey = SHA256.HashData(Encoding.UTF8.GetBytes(rawMaster));
        _activeVersion = config["FieldEncryption:ActiveKeyVersion"] ?? "v1";
    }

    public byte[] GetTenantKey(Guid tenantId, string? keyVersion = null)
    {
        // HKDF derivation (RFC 5869): salt is tenantId bytes, info isolates tenant domain key
        var salt = tenantId.ToByteArray();
        var info = Encoding.UTF8.GetBytes($"MedicalPortal-Tenant-Encryption-Key-{keyVersion ?? _activeVersion}");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _masterKey, 32, salt, info);
    }

    public string GetActiveTenantKeyVersion(Guid tenantId) => _activeVersion;
}
