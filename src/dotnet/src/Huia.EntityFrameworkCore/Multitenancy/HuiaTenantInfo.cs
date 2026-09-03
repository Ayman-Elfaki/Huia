using Finbuckle.MultiTenant.Abstractions;

namespace Huia.EntityFrameworkCore.Multitenancy;

/// <summary>
/// Finbuckle tenant descriptor. Finbuckle 10's <see cref="ITenantInfo"/> carries only an id and an
/// identifier; for Huia both are the tenant key (the base-path segment). Everything else about a tenant
/// lives in the Huia options tree, not here.
/// </summary>
public sealed class HuiaTenantInfo : ITenantInfo
{
    /// <summary>Creates an empty descriptor (required by Finbuckle's stores).</summary>
    public HuiaTenantInfo()
    {
    }

    /// <summary>Creates a descriptor for the given tenant key.</summary>
    /// <param name="tenantId">The tenant key / base-path segment.</param>
    public HuiaTenantInfo(string tenantId)
    {
        Id = tenantId;
        Identifier = tenantId;
    }

    /// <inheritdoc />
    public string Id { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Identifier { get; set; } = string.Empty;
}
