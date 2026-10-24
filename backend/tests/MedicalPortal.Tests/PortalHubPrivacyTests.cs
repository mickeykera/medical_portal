using System.Net.Http.Headers;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class PortalHubPrivacyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PortalHubPrivacyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HubConnection BuildConnection(string? token = null)
    {
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/portal", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                if (!string.IsNullOrEmpty(token))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();
    }

    private (Guid DanaPatientId, Guid StrangerPatientId, string DanaToken, string StrangerToken) SetupTestUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var danaAccount = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "dana@example.com");
        var danaPatient = db.Patients.First(p => p.PersonId == danaAccount.PersonId);

        var strangerEmail = "stranger.signalr@example.com";
        var strangerAccount = db.UserAccounts.Include(u => u.Person).FirstOrDefault(u => u.Email == strangerEmail);
        Patient strangerPatient;

        if (strangerAccount == null)
        {
            var strangerPerson = new Person
            {
                FirstName = "Stranger",
                LastName = "User",
                DateOfBirth = new DateTime(1992, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                Email = strangerEmail
            };
            db.Persons.Add(strangerPerson);

            strangerAccount = new UserAccount
            {
                Person = strangerPerson,
                Email = strangerEmail,
                PasswordHash = "hash",
                Role = UserRole.PATIENT
            };
            db.UserAccounts.Add(strangerAccount);

            strangerPatient = new Patient
            {
                Person = strangerPerson,
                Mrn = "MRN-STRANGER-SIG"
            };
            db.Patients.Add(strangerPatient);
            db.SaveChanges();
        }
        else
        {
            strangerPatient = db.Patients.First(p => p.PersonId == strangerAccount.PersonId);
        }

        var danaToken = db.CreateAuthenticatedSession(tokenService, danaAccount, danaAccount.PersonId);
        var strangerToken = db.CreateAuthenticatedSession(tokenService, strangerAccount, strangerAccount.PersonId);

        return (danaPatient.Id, strangerPatient.Id, danaToken, strangerToken);
    }

    [Fact]
    public async Task Hub_RejectsUnauthenticatedConnectionOrJoin()
    {
        var (danaPatientId, _, _, _) = SetupTestUsers();

        // Connect with no token
        await using var anonConnection = BuildConnection(token: null);

        // Either connection start fails/aborts, or invoking JoinPatientRoom throws
        bool rejected = false;
        try
        {
            await anonConnection.StartAsync();
            await anonConnection.InvokeAsync("JoinPatientRoom", danaPatientId.ToString());
        }
        catch (Exception)
        {
            rejected = true;
        }

        Assert.True(rejected, "Unauthenticated client should be rejected on connection or room join.");
    }

    [Fact]
    public async Task JoinPatientRoom_ThrowsAccessDenied_WhenUserNotAuthorizedForPatient()
    {
        var (_, strangerPatientId, danaToken, _) = SetupTestUsers();

        await using var danaConnection = BuildConnection(danaToken);
        await danaConnection.StartAsync();

        // Dana attempts to join Stranger's patient room
        var ex = await Assert.ThrowsAsync<HubException>(() =>
            danaConnection.InvokeAsync("JoinPatientRoom", strangerPatientId.ToString()));

        Assert.Contains("Access denied", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignalR_Notifications_AreScoped_AndDoNotLeakToUnauthorizedClients()
    {
        var (danaPatientId, strangerPatientId, danaToken, strangerToken) = SetupTestUsers();

        await using var danaConn = BuildConnection(danaToken);
        await using var strangerConn = BuildConnection(strangerToken);

        await danaConn.StartAsync();
        await strangerConn.StartAsync();

        // Dana joins her own room
        await danaConn.InvokeAsync("JoinPatientRoom", danaPatientId.ToString());

        // Stranger joins their own room
        await strangerConn.InvokeAsync("JoinPatientRoom", strangerPatientId.ToString());

        bool danaReceived = false;
        bool strangerReceived = false;

        danaConn.On<object>("NewMessage", _ => { danaReceived = true; });
        strangerConn.On<object>("NewMessage", _ => { strangerReceived = true; });

        // Trigger notification targeted to Dana's patient room / message
        using var scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IPortalNotificationService>();

        await notifier.NotifyNewMessageAsync(Guid.NewGuid(), danaPatientId, new
        {
            text = "Secret confidential clinical message for Dana only"
        });

        // Allow time for event delivery
        await Task.Delay(1000);

        Assert.True(danaReceived, "Authorized patient (Dana) should receive the message notification.");
        Assert.False(strangerReceived, "Unauthorized client (Stranger) MUST NOT receive message notification or metadata.");
    }
}
