using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;

namespace MedicalPortal.Infrastructure.Auth;

public record RecoveryCodeItem(string CodeHash, bool Used);

public interface ITotpService
{
    string GenerateSecret(int byteLength = 20);
    string GenerateQrCodeUri(string email, string base32Secret, string issuer = "MedicalPortal");
    string GenerateTotpCode(string base32Secret, long? timestampSeconds = null);
    bool VerifyTotpCode(string base32Secret, string code, int window = 1);
    List<string> GenerateRecoveryCodes(int count = 8);
    string HashRecoveryCode(string code);
    bool VerifyAndConsumeRecoveryCode(UserAccount account, string code);
}

public class TotpService : ITotpService
{
    private static readonly char[] Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    public string GenerateSecret(int byteLength = 20)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return ToBase32String(bytes);
    }

    public string GenerateQrCodeUri(string email, string base32Secret, string issuer = "MedicalPortal")
    {
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
    }

    public string GenerateTotpCode(string base32Secret, long? timestampSeconds = null)
    {
        var secretBytes = FromBase32String(base32Secret);
        var time = timestampSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timeStep = time / 30;

        var timeBytes = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(timeBytes);
        }

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(timeBytes);

        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

        var code = binaryCode % 1_000_000;
        return code.ToString("D6");
    }

    public bool VerifyTotpCode(string base32Secret, string code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
            return false;

        var trimmedCode = code.Trim().Replace(" ", "").Replace("-", "");
        if (trimmedCode.Length != 6 || !trimmedCode.All(char.IsAsciiDigit))
            return false;

        var inputBytes = Encoding.UTF8.GetBytes(trimmedCode);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (int i = -window; i <= window; i++)
        {
            var stepTime = now + (i * 30);
            var expectedCode = GenerateTotpCode(base32Secret, stepTime);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedCode);

            if (CryptographicOperations.FixedTimeEquals(inputBytes, expectedBytes))
            {
                return true;
            }
        }

        return false;
    }

    public List<string> GenerateRecoveryCodes(int count = 8)
    {
        const string chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // 32 characters, no ambiguous chars
        var codes = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(8);
            var sb = new StringBuilder(9);
            for (int j = 0; j < 8; j++)
            {
                if (j == 4) sb.Append('-');
                sb.Append(chars[bytes[j] % chars.Length]);
            }
            codes.Add(sb.ToString());
        }
        return codes;
    }

    public string HashRecoveryCode(string code)
    {
        var normalized = code.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }

    public bool VerifyAndConsumeRecoveryCode(UserAccount account, string code)
    {
        if (string.IsNullOrWhiteSpace(account.MfaRecoveryCodesJson) || string.IsNullOrWhiteSpace(code))
            return false;

        var targetHash = HashRecoveryCode(code);
        var targetHashBytes = Encoding.UTF8.GetBytes(targetHash);

        List<RecoveryCodeItem>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<RecoveryCodeItem>>(account.MfaRecoveryCodesJson);
        }
        catch
        {
            return false;
        }

        if (items == null || items.Count == 0)
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!item.Used)
            {
                var storedBytes = Encoding.UTF8.GetBytes(item.CodeHash);
                if (CryptographicOperations.FixedTimeEquals(targetHashBytes, storedBytes))
                {
                    items[i] = item with { Used = true };
                    account.MfaRecoveryCodesJson = JsonSerializer.Serialize(items);
                    return true;
                }
            }
        }

        return false;
    }

    public static string ToBase32String(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;
        var sb = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(Base32Chars[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
        {
            sb.Append(Base32Chars[(buffer << (5 - bitsLeft)) & 0x1F]);
        }
        return sb.ToString();
    }

    public static byte[] FromBase32String(string base32)
    {
        if (string.IsNullOrWhiteSpace(base32)) return Array.Empty<byte>();
        var clean = base32.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>((clean.Length * 5) / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (var c in clean)
        {
            int val = c switch
            {
                >= 'A' and <= 'Z' => c - 'A',
                >= '2' and <= '7' => c - '2' + 26,
                _ => throw new FormatException($"Invalid character in Base32 string: {c}")
            };
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }
        return bytes.ToArray();
    }
}
