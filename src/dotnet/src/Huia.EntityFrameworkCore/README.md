# Huia.EntityFrameworkCore

Entity Framework Core persistence for the Huia identity provider:

- `HuiaDbContext` derives from **Finbuckle's `MultiTenantIdentityDbContext<HuiaUser, HuiaRole,
  string>`** and calls `builder.UseOpenIddict()`. Its constructor takes
  `(IMultiTenantContextAccessor accessor, DbContextOptions<HuiaDbContext> options)` and snapshots the
  resolved `TenantInfo` **at construction** — seeding / background code must set the ambient tenant
  (`HuiaTenantScope.Enter(...)`) *before* resolving the context or `UserManager`.
- Tenant isolation is a **global query filter** (`TenantId == TenantInfo.Id`) on every user / role /
  claim / login / token entity, plus write-time `EnforceMultiTenant()` (throws on a tenant-less or
  cross-tenant write, auto-stamps `TenantId` on insert). Any deliberately cross-tenant query must
  `.IgnoreQueryFilters()` — the filter *NREs* rather than returning nothing when no tenant is in
  scope.
- All tables are renamed to `Huia*` via `ToTable`; `ApplyTenantScopedIndexes` runs last in
  `OnModelCreating` and re-adds the named composites the model tests assert
  (`IX_HuiaUsers_Tenant_UserName` unique, `IX_HuiaUsers_Tenant_Email`, `IX_HuiaRoles_Tenant_Name`
  unique), superseding Finbuckle's `AdjustUniqueIndexes`.
- `HuiaSigningKey` is **not** multi-tenant (it carries a manual `TenantId`), so the key-lifecycle
  jobs run with no tenant scope. OpenIddict entities are stock; the tenant binding lives in each
  application's `Properties["huia:tenant"]` and is enforced by `HuiaOpenIddict{Application,Scope}Store`
  (which read `IMultiTenantContextAccessor`).
- `IMultiTenantContextAccessor` extensions `CurrentTenantId()` / `RequireCurrentTenantId()`
  (`Huia.EntityFrameworkCore.Multitenancy.TenantAccessorExtensions`) for reads.

This package is provider-agnostic and **ships no migrations**. The host application owns the provider
package (for example `Npgsql.EntityFrameworkCore.PostgreSQL`), the migrations, and the design-time
factory.
