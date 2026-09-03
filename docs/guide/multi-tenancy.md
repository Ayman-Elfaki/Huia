# Multi-tenancy

Tenants are resolved from the first path segment (`/{tenant}/...`) by Finbuckle's base-path
strategy with `RebaseAspNetCorePathBase`. `UseHuia()` runs `UseMultiTenant()` before `UseRouting()`
and `UseAuthentication()`.

## Data isolation

`HuiaDbContext` derives from Finbuckle's `MultiTenantIdentityDbContext<HuiaUser, HuiaRole, string>`.
Every Identity entity (users, roles, their claims, logins, tokens and role links) carries a
`TenantId` and is:

- **filtered on read** — a global query filter restricts every query to the context's tenant. The
  tenant is snapshotted from the ambient `IMultiTenantContextAccessor` when the context is
  constructed, which during a request is the routing tenant.
- **enforced on write** — `SaveChanges` throws `MultiTenantException` if an Identity entity is added,
  modified or deleted while no tenant is in scope, or while it belongs to a different tenant;
  `TenantId` is stamped automatically on insert.

`HuiaDbContext.OnModelCreating` then strips the default Identity indexes and re-declares explicit
tenant-scoped composites (`IX_HuiaUsers_Tenant_UserName` unique, `IX_HuiaUsers_Tenant_Email`,
`IX_HuiaRoles_Tenant_Name` unique) so a username or email is unique only *within* a tenant.

Code that legitimately reads across tenants — the admin console's user list, for example — calls
`.IgnoreQueryFilters()`. Seeding and other non-request work sets the tenant first:

```csharp
await using var scope = services.CreateAsyncScope();
using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
{
    var users = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
    // ...resolved after Enter, so HuiaDbContext binds to `tenantId`
}
```

The OpenIddict entities stay tenant-agnostic: a client's or scope's tenant lives in its
`Properties["huia:tenant"]` entry, and `HuiaOpenIddictApplicationStore` / `HuiaOpenIddictScopeStore`
make rows from other tenants invisible on the lookups the authorize / token endpoints depend on.
`HuiaSigningKey` also keeps a plain `TenantId` column (not a multi-tenant entity) so the key
lifecycle jobs can run with no tenant in scope.

## Authentication isolation

`AddMultiTenant<HuiaTenantInfo>().WithPerTenantAuthentication()` binds every interactive login
session to the tenant it was created under: the authentication ticket records its origin tenant, and
a request whose resolved tenant does not match has its principal rejected. On top of that, each
tenant gets its own cookie name (`huia.auth.{tenant}`, `huia.2fa.{tenant}`) so one browser can hold
several tenants' sessions at once. Tokens are additionally isolated by a per-tenant issuer
(`{issuer}/{tenant}`) and per-tenant rotated signing keys.

## Migrations

Huia ships no migrations. Adopters on a real EF Core provider must generate one migration after
upgrading to pick up the `TenantId` shadow columns and composite indexes Finbuckle adds to the
Identity join/claim tables:

```bash
dotnet ef migrations add PerTenantIdentity
```
