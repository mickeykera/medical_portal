using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Web;

namespace MedicalPortal.Tests;

public sealed class NoOpDatabaseRoleInspector : IDatabaseRoleInspector
{
    public Task InspectAsync(MedicalPortalDbContext db, string connectionString) => Task.CompletedTask;
}
