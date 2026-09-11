namespace Huia.Multitenancy;

/// <summary>
/// The ambient-tenant seam shared code (destined for reuse by both the multi-tenant OpenId flavor and the
/// single-tenant Headless flavor of Huia) depends on instead of a specific multi-tenancy implementation.
/// <c>Huia.OpenId</c> registers an implementation backed by Finbuckle; a single-tenant host registers a
/// trivial constant-tenant, no-op-scope implementation.
/// </summary>
public interface IHuiaTenantContext
{
    /// <summary>The current tenant identifier.</summary>
    /// <exception cref="InvalidOperationException">No tenant is in scope.</exception>
    string CurrentTenantId { get; }

    /// <summary>The current tenant identifier, or <see langword="null"/> when none is in scope (for example
    /// a request whose path resolved to no tenant segment). Prefer <see cref="CurrentTenantId"/> wherever a
    /// tenant is always expected to be in scope; use this only where "no tenant" is a legitimate outcome.</summary>
    string? CurrentTenantIdOrDefault { get; }

    /// <summary>
    /// Sets the ambient tenant for a block of work outside an HTTP request — seeding, background jobs,
    /// administrative operations that touch another tenant. Callers must enter the scope <em>before</em>
    /// resolving a <c>DbContext</c> or the Identity managers from <paramref name="scopedServices"/>.
    /// </summary>
    /// <param name="scopedServices">A service provider (typically a fresh DI scope) to bind the tenant in.</param>
    /// <param name="tenantId">The tenant identifier to enter.</param>
    /// <returns>A disposable that restores the previous ambient tenant.</returns>
    IDisposable EnterTenantScope(IServiceProvider scopedServices, string tenantId);
}
