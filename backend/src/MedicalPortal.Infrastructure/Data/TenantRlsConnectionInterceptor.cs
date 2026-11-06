using System.Data.Common;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MedicalPortal.Infrastructure.Data;

internal sealed class TenantRlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;

    public TenantRlsConnectionInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        base.ConnectionOpened(connection, eventData);
        SetTenant(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        await SetTenantAsync(connection, cancellationToken);
    }

    private void SetTenant(DbConnection connection)
    {
        if (connection.GetType().Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) != true)
            return;

        var tenantId = _tenantContext.IsResolved
            ? _tenantContext.TenantId
            : TenantConstants.DefaultTenantId;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_tenant_id', @tenant_id, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant_id";
        parameter.Value = tenantId.ToString();
        command.Parameters.Add(parameter);
        command.ExecuteNonQuery();
    }

    private async Task SetTenantAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.GetType().Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) != true)
            return;

        var tenantId = _tenantContext.IsResolved
            ? _tenantContext.TenantId
            : TenantConstants.DefaultTenantId;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_tenant_id', @tenant_id, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant_id";
        parameter.Value = tenantId.ToString();
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
