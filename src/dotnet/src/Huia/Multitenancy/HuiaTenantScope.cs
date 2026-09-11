using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Multitenancy;

/// <summary>
/// Sets the ambient Finbuckle tenant for a block of work outside an HTTP request — seeding, background
/// jobs, administrative operations that touch another tenant. Because <c>HuiaDbContext</c> snapshots the
/// tenant when it is constructed, callers must enter the scope <em>before</em> resolving
/// <c>HuiaDbContext</c> or the Identity managers from the same service provider.
/// </summary>
public static class HuiaTenantScope
{
    /// <summary>Enters a tenant scope, returning a handle that restores the previous ambient tenant on dispose.</summary>
    /// <param name="scopedServices">A service provider (typically a fresh DI scope) whose Finbuckle context to set.</param>
    /// <param name="tenantId">The tenant identifier to enter.</param>
    /// <returns>A disposable that restores the previous ambient tenant.</returns>
    public static IDisposable Enter(IServiceProvider scopedServices, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var accessor = scopedServices.GetRequiredService<IMultiTenantContextAccessor>();
        var setter = scopedServices.GetRequiredService<IMultiTenantContextSetter>();

        var previous = accessor.MultiTenantContext;
        setter.MultiTenantContext = new MultiTenantContext<HuiaTenantInfo>(new HuiaTenantInfo(tenantId));

        return new Restorer(setter, previous);
    }

    private sealed class Restorer(IMultiTenantContextSetter setter, IMultiTenantContext previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            setter.MultiTenantContext = previous;
        }
    }
}
