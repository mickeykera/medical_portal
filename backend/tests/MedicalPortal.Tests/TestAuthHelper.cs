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
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var session = new UserSession
        {
            TenantId = account.TenantId,
            UserAccountId = account.Id,
            Sid = sid,
            RefreshTokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sid))),
            CreatedAt = now,
            LastActiveAt = now,
            ExpiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7), DateTimeKind.Unspecified)
        };
        db.UserSessions.Add(session);
        db.SaveChanges();

        return tokens.GenerateAccessToken(account, personId ?? account.PersonId, sid);
    }
}
