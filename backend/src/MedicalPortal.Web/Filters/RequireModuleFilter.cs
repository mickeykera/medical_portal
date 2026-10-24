using MedicalPortal.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MedicalPortal.Web.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class RequireModuleAttribute : TypeFilterAttribute
{
    public RequireModuleAttribute(string moduleName) : base(typeof(RequireModuleFilter))
    {
        Arguments = new object[] { moduleName };
    }
}

public class RequireModuleFilter : IAsyncActionFilter
{
    private readonly string _moduleName;
    private readonly ITenantContext _tenantContext;

    public RequireModuleFilter(string moduleName, ITenantContext tenantContext)
    {
        _moduleName = moduleName;
        _tenantContext = tenantContext;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_tenantContext.CurrentTenant != null)
        {
            var settings = _tenantContext.CurrentTenant.GetParsedSettings();
            if (!settings.Modules.IsEnabled(_moduleName))
            {
                context.Result = new ObjectResult(new
                {
                    error = $"The module '{_moduleName}' is disabled for this hospital tenant.",
                    code = "module_disabled",
                    module = _moduleName
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }
        }

        await next();
    }
}
