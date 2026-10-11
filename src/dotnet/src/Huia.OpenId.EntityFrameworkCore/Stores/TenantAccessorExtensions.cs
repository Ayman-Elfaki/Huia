using Finbuckle.MultiTenant.Abstractions;

namespace Huia.OpenId.EntityFrameworkCore.Stores;

internal static class TenantAccessorExtensions
{
    /// <summary>The identifier of the tenant currently in scope, or <c>null</c> when there is none.</summary>
    public static string? CurrentTenantId(this IMultiTenantContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return accessor.MultiTenantContext.TenantInfo?.Identifier;
    }
}
