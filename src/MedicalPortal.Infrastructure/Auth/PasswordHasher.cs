using System.Security.Cryptography;
using System.Text;

namespace MedicalPortal.Infrastructure.Auth;

public interface IPasswordHasher
{
    string HashPassword(string plain);
    bool VerifyPassword(string hash, string plain);
    (bool Valid, string? Reason) ValidatePasswordStrength(string plain);
    Task VerifyDummyPasswordAsync(string plain);
}

public class PasswordHasher : IPasswordHasher
{
    // A constant dummy hash to equalize timing on unknown account paths
    private const string DummyHash = "$2a$11$N9qo8uLOickgx2ZMRZoMye.IuNlTz6E3Yc1nJd3bW8x7uGf2v4m5K";

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "123456", "12345678", "123456789",
        "qwerty", "qwerty123", "letmein", "welcome", "admin", "iloveyou",
        "monkey", "dragon", "sunshine", "princess", "football", "abc123"
    };

    public string HashPassword(string plain) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(plain, 11);

    public bool VerifyPassword(string hash, string plain)
    {
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(plain, hash);
        }
        catch
        {
            return false;
        }
    }

    public Task VerifyDummyPasswordAsync(string plain)
    {
        return Task.Run(() =>
        {
            try { BCrypt.Net.BCrypt.EnhancedVerify(plain, DummyHash); }
            catch { }
        });
    }

    public (bool Valid, string? Reason) ValidatePasswordStrength(string plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.Length < 12)
            return (false, "Password must be at least 12 characters.");

        if (plain.Length > 200)
            return (false, "Password must be under 200 characters.");

        if (CommonPasswords.Contains(plain))
            return (false, "This password is too common. Choose something less predictable.");

        return (true, null);
    }
}
