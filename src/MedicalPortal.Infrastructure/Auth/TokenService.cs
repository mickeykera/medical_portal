using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MedicalPortal.Infrastructure.Auth;

public record AccessTokenClaims(Guid UserAccountId, Guid PersonId, UserRole Role, string Sid);

public interface ITokenService
{
    string GenerateAccessToken(UserAccount account, Guid personId, string? sessionId = null);
    AccessTokenClaims? ValidateAccessToken(string token);
    string GenerateMfaChallengeToken(Guid userAccountId);
    Guid? ValidateMfaChallengeToken(string token);
    int ExpirationMinutes { get; }
}

public class TokenService : ITokenService
{
    private const string Issuer = "medical-portal";
    private const string Audience = "medical-portal-api";

    private readonly byte[] _key;
    public int ExpirationMinutes { get; }

    public TokenService(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"] ?? configuration["JWT_SECRET"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("JWT secret is not configured. Set 'Jwt:Secret' or 'JWT_SECRET' in configuration or environment variables.");
        }

        if (Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException("JWT secret must be at least 32 bytes (256 bits) for HMAC-SHA256 security.");
        }

        _key = Encoding.UTF8.GetBytes(secret);

        if (int.TryParse(configuration["Jwt:ExpirationMinutes"] ?? configuration["ACCESS_TOKEN_TTL_MINUTES"], out var mins) && mins > 0)
        {
            ExpirationMinutes = mins;
        }
        else
        {
            ExpirationMinutes = 15;
        }
    }

    public string GenerateAccessToken(UserAccount account, Guid personId, string? sessionId = null)
    {
        var sid = sessionId ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var claims = new[]
        {
            new Claim("userAccountId", account.Id.ToString()),
            new Claim("personId", personId.ToString()),
            new Claim(ClaimTypes.Role, account.Role.ToString()),
            new Claim("role", account.Role.ToString()),
            new Claim("sid", sid),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(ExpirationMinutes),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public AccessTokenClaims? ValidateAccessToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_key),
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var userAccountIdStr = principal.FindFirst("userAccountId")?.Value;
            var personIdStr = principal.FindFirst("personId")?.Value;
            var roleStr = principal.FindFirst("role")?.Value ?? principal.FindFirst(ClaimTypes.Role)?.Value;
            var sid = principal.FindFirst("sid")?.Value ?? string.Empty;

            if (Guid.TryParse(userAccountIdStr, out var userAccountId) &&
                Guid.TryParse(personIdStr, out var personId) &&
                Enum.TryParse<UserRole>(roleStr, true, out var role))
            {
                return new AccessTokenClaims(userAccountId, personId, role, sid);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public string GenerateMfaChallengeToken(Guid userAccountId)
    {
        var claims = new[]
        {
            new Claim("sub", userAccountId.ToString()),
            new Claim("purpose", "mfa_challenge"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(5), // 5 minute challenge window
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public Guid? ValidateMfaChallengeToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_key),
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var purpose = principal.FindFirst("purpose")?.Value;
            if (purpose != "mfa_challenge")
                return null;

            var subStr = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(subStr, out var userAccountId))
            {
                return userAccountId;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
