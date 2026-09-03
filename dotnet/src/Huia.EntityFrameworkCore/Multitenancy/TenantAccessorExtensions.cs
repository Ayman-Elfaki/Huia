using Finbuckle.MultiTenant.Abstractions;

namespace Huia.EntityFrameworkCore.Multitenancy;

/// <summary>
/// Convenience readers over Finbuckle's <see cref="IMultiTenantContextAccessor"/>: the identifier of the
/// tenant the current request (or explicit scope) resolved to.
/// </summary>
public static class TenantAccessorExtensions
{
    /// <summary>The current tenant identifier, or <see langword="null"/> when none is in scope.</summary>
    /// <param name="accessor">The Finbuckle context accessor.</param>
    /// <returns>The tenant identifier or <see langword="null"/>.</returns>
    public static string? CurrentTenantId(this IMultiTenantContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return accessor.MultiTenantContext.TenantInfo?.Identifier;
    }

    /// <summary>The current tenant identifier, or throws when none is in scope.</summary>
    /// <param name="accessor">The Finbuckle context accessor.</param>
    /// <returns>The tenant identifier.</returns>
    /// <exception cref="InvalidOperationException">No tenant is in scope.</exception>
    public static string RequireCurrentTenantId(this IMultiTenantContextAccessor accessor) =>
        accessor.CurrentTenantId() ?? throw new InvalidOperationException(
            "No Huia tenant is in scope. Inside a request the routing tenant supplies it; elsewhere set " +
            "it with HuiaTenantScope.Enter(tenantId) before resolving HuiaDbContext or the Identity managers.");
}
