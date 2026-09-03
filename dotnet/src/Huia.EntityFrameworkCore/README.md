# Huia.EntityFrameworkCore

Entity Framework Core persistence for the Huia identity provider:

- `HuiaDbContext` — inherits `IdentityDbContext<HuiaUser, HuiaRole, string>` and calls `builder.UseOpenIddict()`.
- All tables renamed to `Huia*`; the default Identity global unique indexes are stripped and replaced
  with tenant-scoped composite indexes.
- `HuiaUserStore`, `HuiaRoleStore` and `HuiaOpenIddictApplicationStore` append an explicit
  `TenantId` predicate to every query — there is no ambient tenant and no global query filter.

This package is provider-agnostic and **ships no migrations**. The host application owns the provider
package (for example `Npgsql.EntityFrameworkCore.PostgreSQL`), the migrations and the design-time
factory.
