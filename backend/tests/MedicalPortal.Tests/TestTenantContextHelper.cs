using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace MedicalPortal.Tests;

public static class TestTenantContextHelper
{
    public static IDbContextTransaction BeginTestTransaction(this MedicalPortalDbContext db, Guid tenantId)
    {
        var transaction = db.Database.BeginTransaction();
        if (db.Database.IsNpgsql())
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }
            using var command = transaction.GetDbTransaction().Connection!.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT set_config('app.current_tenant_id', '{tenantId}', true)";
            command.ExecuteNonQuery();
        }
        return transaction;
    }

    public static async Task<IDbContextTransaction> BeginTestTransactionAsync(this MedicalPortalDbContext db, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }
            await using var command = transaction.GetDbTransaction().Connection!.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT set_config('app.current_tenant_id', '{tenantId}', true)";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return transaction;
    }

    public static IDbContextTransaction BeginDefaultTestTransaction(this MedicalPortalDbContext db)
    {
        var defaultTenantId = MedicalPortal.Core.Entities.TenantConstants.DefaultTenantId;
        return db.BeginTestTransaction(defaultTenantId);
    }

    public static async Task<IDbContextTransaction> BeginDefaultTestTransactionAsync(this MedicalPortalDbContext db, CancellationToken cancellationToken = default)
    {
        var defaultTenantId = MedicalPortal.Core.Entities.TenantConstants.DefaultTenantId;
        return await db.BeginTestTransactionAsync(defaultTenantId, cancellationToken);
    }
}
