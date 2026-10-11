namespace Huia.Stores;

/// <summary>A registered OAuth client as the admin console lists it.</summary>
/// <param name="Id">The store's primary key for the client.</param>
/// <param name="ClientId">The public OAuth client identifier.</param>
/// <param name="DisplayName">The display name, if any.</param>
/// <param name="ClientType">The OpenIddict client type (<c>confidential</c> / <c>public</c>).</param>
/// <param name="Properties">The raw OpenIddict properties JSON, carrying the <c>huia:tenant</c> / <c>huia:origin</c> markers.</param>
public sealed record HuiaClientRecord(string Id, string? ClientId, string? DisplayName, string? ClientType, string? Properties);

/// <summary>A registered scope as the admin console lists it.</summary>
/// <param name="Id">The store's primary key for the scope.</param>
/// <param name="Name">The scope name.</param>
/// <param name="DisplayName">The display name, if any.</param>
/// <param name="Description">The description, if any.</param>
/// <param name="Properties">The raw OpenIddict properties JSON, carrying the <c>huia:tenant</c> / <c>huia:origin</c> markers.</param>
public sealed record HuiaScopeRecord(string Id, string? Name, string? DisplayName, string? Description, string? Properties);

/// <summary>Keyset-paginated listing parameters for <see cref="IHuiaOpenIddictAdminStore.ListClientsAsync"/>.</summary>
public sealed record HuiaClientQuery
{
    /// <summary>Page size.</summary>
    public int PageSize { get; init; } = 25;

    /// <summary>Return the rows after this client id (the store's primary key).</summary>
    public string? After { get; init; }

    /// <summary>Return the rows before this client id (the store's primary key).</summary>
    public string? Before { get; init; }
}

/// <summary>
/// Cross-tenant, read-only view of the OpenIddict clients and scopes for the admin console. Mutations go
/// through OpenIddict's own managers (inside the target tenant's scope); this port only covers the reads
/// that need to see every tenant's rows regardless of the ambient tenant.
/// </summary>
public interface IHuiaOpenIddictAdminStore
{
    /// <summary>Returns a keyset-paginated page of clients across all tenants, ordered by id.</summary>
    Task<IHuiaPageResult<HuiaClientRecord>> ListClientsAsync(HuiaClientQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns one client by its primary key across all tenants, or <c>null</c>.</summary>
    Task<HuiaClientRecord?> FindClientAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Returns every scope across all tenants, ordered by name.</summary>
    Task<IReadOnlyList<HuiaScopeRecord>> ListScopesAsync(CancellationToken cancellationToken = default);
}
