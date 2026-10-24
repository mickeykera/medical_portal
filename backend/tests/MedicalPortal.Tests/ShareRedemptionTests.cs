using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class ShareRedemptionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ShareRedemptionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(RecordShare share, string rawCode)> CreateShareAsync(
        Action<RecordShare>? configure = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var patient = await db.Patients.Include(p => p.Person).FirstAsync();

        var rawCode = $"SHR-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawCode)));

        var share = new RecordShare
        {
            PatientId = patient.Id,
            Label = "Test Medical Records Share",
            CodeHash = codeHash,
            RecipientName = "Dr. External Specialist",
            RecipientRole = "Consulting Physician",
            ScopeJson = "[\"allergies\",\"conditions\",\"medications\",\"labs\",\"vitals\"]",
            MaxUses = 3,
            UseCount = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            RevokedAt = null
        };

        configure?.Invoke(share);

        db.RecordShares.Add(share);
        await db.SaveChangesAsync();

        return (share, rawCode);
    }

    [Fact]
    public async Task AnonymousCaller_CanRedeemValidShareCode_WithoutAuthentication()
    {
        var (share, rawCode) = await CreateShareAsync();

        // Create unauthenticated HTTP client (no session cookie, no bearer token)
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/share", new { code = rawCode });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bundle = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bundle", bundle.GetProperty("resourceType").GetString());

        // Verify UseCount was incremented atomically
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updatedShare = await db.RecordShares.FindAsync(share.Id);
        Assert.NotNull(updatedShare);
        Assert.Equal(1, updatedShare.UseCount);

        // Verify RecordShareAccess entry was logged
        var access = await db.RecordShareAccesses.FirstOrDefaultAsync(a => a.ShareId == share.Id);
        Assert.NotNull(access);

        // Verify AuditLog entry was recorded
        var audit = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "SHARE_REDEEMED" && a.ResourceId == share.Id.ToString());
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Redeem_ExpiredCode_ReturnsGenericError_AndLogsAudit()
    {
        var (share, rawCode) = await CreateShareAsync(s =>
        {
            s.ExpiresAt = DateTime.UtcNow.AddDays(-2); // Expired 2 days ago
        });

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/share", new { code = rawCode });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid or expired share code.", body.GetProperty("error").GetString());

        // Verify UseCount was NOT incremented
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updatedShare = await db.RecordShares.FindAsync(share.Id);
        Assert.NotNull(updatedShare);
        Assert.Equal(0, updatedShare.UseCount);

        // Verify AuditLog was recorded for failed attempt
        var audit = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "SHARE_REDEMPTION_FAILED" && a.ResourceId == share.Id.ToString());
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Redeem_RevokedCode_ReturnsGenericError_AndLogsAudit()
    {
        var (share, rawCode) = await CreateShareAsync(s =>
        {
            s.RevokedAt = DateTime.UtcNow.AddHours(-1); // Revoked 1 hour ago
        });

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/share", new { code = rawCode });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid or expired share code.", body.GetProperty("error").GetString());

        // Verify UseCount was NOT incremented
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updatedShare = await db.RecordShares.FindAsync(share.Id);
        Assert.NotNull(updatedShare);
        Assert.Equal(0, updatedShare.UseCount);

        // Verify AuditLog was recorded for failed attempt
        var audit = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "SHARE_REDEMPTION_FAILED" && a.ResourceId == share.Id.ToString());
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Redeem_ExhaustedCode_ReturnsGenericError_AndLogsAudit()
    {
        var (share, rawCode) = await CreateShareAsync(s =>
        {
            s.MaxUses = 2;
            s.UseCount = 2; // Already reached max uses
        });

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/share", new { code = rawCode });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid or expired share code.", body.GetProperty("error").GetString());

        // Verify UseCount remains unchanged
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updatedShare = await db.RecordShares.FindAsync(share.Id);
        Assert.NotNull(updatedShare);
        Assert.Equal(2, updatedShare.UseCount);

        // Verify AuditLog was recorded for failed attempt
        var audit = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "SHARE_REDEMPTION_FAILED" && a.ResourceId == share.Id.ToString());
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Redeem_NonExistentCode_ReturnsGenericError_AndLogsAudit()
    {
        var nonExistentCode = "SHR-999999999999";

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/share", new { code = nonExistentCode });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid or expired share code.", body.GetProperty("error").GetString());

        // Verify AuditLog was recorded for failed attempt
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var audit = await db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.Action == "SHARE_REDEMPTION_FAILED");
        Assert.NotNull(audit);
    }
}
