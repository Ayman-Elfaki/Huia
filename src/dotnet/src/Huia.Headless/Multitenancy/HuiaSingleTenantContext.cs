using Huia.Multitenancy;

namespace Huia.Headless.Multitenancy;

/// <summary>
/// The trivial <see cref="IHuiaTenantContext"/> for the single-tenant Headless flavor: a constant tenant
/// id (the one tenant the host configured) and a no-op scope, since there is never a second tenant to
/// switch into.
/// </summary>
internal sealed class HuiaSingleTenantContext(string tenantId) : IHuiaTenantContext
{
    /// <inheritdoc />
    public string CurrentTenantId => tenantId;

    /// <inheritdoc />
    public string? CurrentTenantIdOrDefault => tenantId;

    /// <inheritdoc />
    public IDisposable EnterTenantScope(IServiceProvider scopedServices, string tenantId) => NoopScope.Instance;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
