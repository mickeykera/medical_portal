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
        // If there's already an active transaction, just set the tenant context on it
        var existingTransaction = db.Database.CurrentTransaction;
        if (existingTransaction != null)
        {
            if (db.Database.IsNpgsql())
            {
                var connection = db.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }
                using var command = existingTransaction.GetDbTransaction().Connection!.CreateCommand();
                command.Transaction = existingTransaction.GetDbTransaction();
                command.CommandText = $"SELECT set_config('app.current_tenant_id', '{tenantId}', true)";
                command.ExecuteNonQuery();
            }
            // Also set the ITenantContext for the DbContext
            db.SetTestTenantContext(tenantId);
            return new NoOpTransaction(existingTransaction);
        }

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
        // Also set the ITenantContext for the DbContext
        db.SetTestTenantContext(tenantId);
        return transaction;
    }

    public static async Task<IDbContextTransaction> BeginTestTransactionAsync(this MedicalPortalDbContext db, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // If there's already an active transaction, just set the tenant context on it
        var existingTransaction = db.Database.CurrentTransaction;
        if (existingTransaction != null)
        {
            if (db.Database.IsNpgsql())
            {
                var connection = db.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken);
                }
                await using var command = existingTransaction.GetDbTransaction().Connection!.CreateCommand();
                command.Transaction = existingTransaction.GetDbTransaction();
                command.CommandText = $"SELECT set_config('app.current_tenant_id', '{tenantId}', true)";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            // Also set the ITenantContext for the DbContext
            db.SetTestTenantContext(tenantId);
            return new NoOpTransaction(existingTransaction);
        }

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
        // Also set the ITenantContext for the DbContext
        db.SetTestTenantContext(tenantId);
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

    public static IDbContextTransaction BeginPlatformTestTransaction(this MedicalPortalDbContext db)
    {
        var platformTenantId = MedicalPortal.Core.Entities.TenantConstants.PlatformTenantId;
        return db.BeginTestTransaction(platformTenantId);
    }

    public static async Task<IDbContextTransaction> BeginPlatformTestTransactionAsync(this MedicalPortalDbContext db, CancellationToken cancellationToken = default)
    {
        var platformTenantId = MedicalPortal.Core.Entities.TenantConstants.PlatformTenantId;
        return await db.BeginTestTransactionAsync(platformTenantId, cancellationToken);
    }

    // Wrapper that delegates to an existing transaction but doesn't own it
    private sealed class NoOpTransaction : IDbContextTransaction
    {
        private readonly IDbContextTransaction _inner;

        public NoOpTransaction(IDbContextTransaction inner)
        {
            _inner = inner;
        }

        public Guid TransactionId => _inner.TransactionId;

        public void Commit() { /* No-op: outer transaction will commit */ }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Rollback() { /* No-op: outer transaction will rollback */ }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { /* No-op: outer transaction will dispose */ }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
