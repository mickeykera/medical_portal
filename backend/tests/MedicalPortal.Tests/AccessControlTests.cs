using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedicalPortal.Tests;

public class AccessControlTests
{
    private MedicalPortalDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MedicalPortalDbContext(options);
    }

    [Fact]
    public async Task ResolveAccess_OwnChart_ReturnsAllowed()
    {
        using var db = CreateInMemoryDb();
        var personId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        db.Persons.Add(new Person { Id = personId, FirstName = "Dana", LastName = "Rivera" });
        db.Patients.Add(new Patient { Id = patientId, PersonId = personId, Mrn = "MRN-1" });
        await db.SaveChangesAsync();

        var service = new AccessControlService(db);
        var actor = new AccessTokenClaims(Guid.NewGuid(), personId, UserRole.PATIENT, "sid1");

        var decision = await service.ResolveAccessAsync(actor, patientId, Permission.viewRecords);
        Assert.True(decision.Allowed);
        Assert.False(decision.IsProxy);
    }

    [Fact]
    public async Task ResolveAccess_ProxyGrant_EnforcesScopedPermissions()
    {
        using var db = CreateInMemoryDb();
        var parentPersonId = Guid.NewGuid();
        var childPersonId = Guid.NewGuid();
        var childPatientId = Guid.NewGuid();

        db.Persons.Add(new Person { Id = parentPersonId, FirstName = "Dana", LastName = "Rivera" });
        db.Persons.Add(new Person { Id = childPersonId, FirstName = "Mateo", LastName = "Rivera" });
        db.Patients.Add(new Patient { Id = childPatientId, PersonId = childPersonId, Mrn = "MRN-2" });

        // Grant with canViewRecords = true, canPayBills = false
        db.ProxyGrants.Add(new ProxyGrant
        {
            GrantorId = childPersonId,
            GranteeId = parentPersonId,
            CanViewRecords = true,
            CanPayBills = false,
            CanMessage = true,
            ConsentGivenAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AccessControlService(db);
        var parentActor = new AccessTokenClaims(Guid.NewGuid(), parentPersonId, UserRole.PATIENT, "sid1");

        // View records: allowed via proxy
        var viewDecision = await service.ResolveAccessAsync(parentActor, childPatientId, Permission.viewRecords);
        Assert.True(viewDecision.Allowed);
        Assert.True(viewDecision.IsProxy);

        // Pay bills: denied
        var billDecision = await service.ResolveAccessAsync(parentActor, childPatientId, Permission.payBills);
        Assert.False(billDecision.Allowed);
        Assert.Equal("permission_not_granted", billDecision.Reason);
    }

    [Fact]
    public async Task ResolveAccess_Clinician_RequiresCareTeamMembership()
    {
        using var db = CreateInMemoryDb();
        var clinicianAccountId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var provider = new Provider
        {
            Id = providerId,
            AccountId = clinicianAccountId,
            FirstName = "Amara",
            LastName = "Patel"
        };
        db.Providers.Add(provider);
        db.Patients.Add(new Patient { Id = patientId, PersonId = Guid.NewGuid(), Mrn = "MRN-1" });
        await db.SaveChangesAsync();

        var service = new AccessControlService(db);
        var clinicianActor = new AccessTokenClaims(clinicianAccountId, Guid.NewGuid(), UserRole.CLINICIAN, "sid1");

        // Not on care team yet
        var decisionBefore = await service.ResolveAccessAsync(clinicianActor, patientId, Permission.viewRecords);
        Assert.False(decisionBefore.Allowed);
        Assert.Equal("not_on_care_team", decisionBefore.Reason);

        // Add to care team
        db.CareTeams.Add(new PatientCareTeam { PatientId = patientId, ProviderId = providerId });
        await db.SaveChangesAsync();

        var decisionAfter = await service.ResolveAccessAsync(clinicianActor, patientId, Permission.viewRecords);
        Assert.True(decisionAfter.Allowed);
    }
}
