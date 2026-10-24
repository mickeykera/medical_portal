using System.Security.Cryptography;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;

namespace MedicalPortal.Tests;

public static class TestAuthHelper
{
    public static string CreateAuthenticatedSession(
        this MedicalPortalDbContext db,
        ITokenService tokens,
        UserAccount account,
        Guid? personId = null)
    {
        var sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var session = new UserSession
        {
            TenantId = account.TenantId,
            UserAccountId = account.Id,
            Sid = sid,
            RefreshTokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sid))),
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        db.UserSessions.Add(session);
        db.SaveChanges();

        return tokens.GenerateAccessToken(account, personId ?? account.PersonId, sid);
    }
}
