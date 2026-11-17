using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Web;

namespace MedicalPortal.Tests;

public sealed class FakeDatabaseRoleInspector : IDatabaseRoleInspector
{
    public string? FailWith { get; init; }

    public Task InspectAsync(MedicalPortalDbContext db, string connectionString)
    {
        if (FailWith is not null)
            throw new InvalidOperationException(FailWith);
        return Task.CompletedTask;
    }
}
