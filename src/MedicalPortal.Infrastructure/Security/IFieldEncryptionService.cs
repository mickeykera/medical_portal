namespace MedicalPortal.Infrastructure.Security;

public interface IKeyProvider
{
    string ActiveKeyVersion { get; }
    byte[] GetKey(string keyVersion);
    bool HasKey(string keyVersion);
    IEnumerable<string> AvailableKeyVersions { get; }
}

public interface IFieldEncryptionService
{
    string? Encrypt(string? plainText);
    string? Decrypt(string? cipherText);
    bool IsEncrypted(string? text);
    string? RotateKey(string? cipherText);
}
