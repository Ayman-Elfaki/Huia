using Finbuckle.MultiTenant.Abstractions;
using Huia.Multitenancy;
using Huia.OpenId.EntityFrameworkCore.Multitenancy;

namespace Huia.OpenId.Multitenancy;

/// <summary>The <see cref="IHuiaTenantContext"/> implementation for the multi-tenant OpenId flavor of Huia,
/// backed by Finbuckle. <see cref="EnterTenantScope"/> delegates to <see cref="HuiaTenantScope.Enter"/>.</summary>
internal sealed class HuiaFinbuckleTenantContext(IMultiTenantContextAccessor accessor) : IHuiaTenantContext
{
    /// <inheritdoc />
    public string CurrentTenantId => accessor.RequireCurrentTenantId();

    /// <inheritdoc />
    public IDisposable EnterTenantScope(IServiceProvider scopedServices, string tenantId) =>
        HuiaTenantScope.Enter(scopedServices, tenantId);
}
