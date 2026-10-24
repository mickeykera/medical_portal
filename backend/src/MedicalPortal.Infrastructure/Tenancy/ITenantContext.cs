using MedicalPortal.Core.Entities;

namespace MedicalPortal.Infrastructure.Tenancy;

public interface ITenantContext
{
    Guid? TenantId { get; }
    Tenant? CurrentTenant { get; }
    bool IsResolved { get; }
    void SetTenant(Tenant tenant);
    void Clear();
}

public class TenantContext : ITenantContext
{
    public Guid? TenantId => CurrentTenant?.Id;
    public Tenant? CurrentTenant { get; private set; }
    public bool IsResolved => CurrentTenant != null;

    public void SetTenant(Tenant tenant)
    {
        CurrentTenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
    }

    public void Clear()
    {
        CurrentTenant = null;
    }
}
