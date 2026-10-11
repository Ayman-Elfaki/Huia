using Finbuckle.MultiTenant.Abstractions;
using Huia.Multitenancy;

namespace Huia.OpenId.Multitenancy;

/// <summary>
/// Finbuckle's tenant store, answered from <see cref="IHuiaTenantProvider"/> so a tenant that appears at runtime
/// (persisted through the tenant store) resolves from the request path without a restart. Read-only: tenants are
/// created through Huia's own tenant store, never through Finbuckle.
/// </summary>
internal sealed class HuiaProviderTenantStore(IHuiaTenantProvider tenants) : IMultiTenantStore<HuiaTenantInfo>
{
    public Task<HuiaTenantInfo?> GetAsync(string id) => Task.FromResult(Find(id));

    public Task<HuiaTenantInfo?> GetByIdentifierAsync(string identifier) => Task.FromResult(Find(identifier));

    public Task<IEnumerable<HuiaTenantInfo>> GetAllAsync() =>
        Task.FromResult<IEnumerable<HuiaTenantInfo>>([.. tenants.Keys.Select(id => new HuiaTenantInfo(id))]);

    public Task<IEnumerable<HuiaTenantInfo>> GetAllAsync(int take, int skip) =>
        Task.FromResult<IEnumerable<HuiaTenantInfo>>(
            [.. tenants.Keys.OrderBy(id => id, StringComparer.Ordinal).Skip(skip).Take(take)
                .Select(id => new HuiaTenantInfo(id))]);

    public Task<bool> AddAsync(HuiaTenantInfo tenantInfo) => Task.FromResult(false);

    public Task<bool> UpdateAsync(HuiaTenantInfo tenantInfo) => Task.FromResult(false);

    public Task<bool> RemoveAsync(string identifier) => Task.FromResult(false);

    private HuiaTenantInfo? Find(string identifier) =>
        tenants.ContainsKey(identifier) ? new HuiaTenantInfo(identifier) : null;
}
