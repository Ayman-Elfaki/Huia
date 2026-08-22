# Custom stores

`Huia.EntityFrameworkCore` is the default persistence provider, but it isn't required — Huia's persistence
is provider-agnostic.

## `IHuiaStore` — Identity, OpenIddict, and key data

`IHuiaStore<TApplication, TAuthorization, TScope, TToken>` composes ASP.NET Core Identity's user/role
stores, OpenIddict's application/authorization/scope/token stores, and `ISigningKeyStore` (signing/encryption
key storage) into one interface. Implement it against whatever you're using (Dapper, MongoDB, ...) and
register it instead of `.WithEntityFrameworkStores<TContext>()`:

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.KeysManagement.UseAutomaticKeyManagement();
    })
    .WithStore<MyStore, MyApplication, MyAuthorization, MyScope, MyToken>();

public class MyStore : IHuiaStore<MyApplication, MyAuthorization, MyScope, MyToken>
{
    // IUserStore<HuiaUser>, IUserLoginStore<HuiaUser>, IRoleStore<HuiaRole>,
    // IOpenIddictApplicationStore<MyApplication>, IOpenIddictAuthorizationStore<MyAuthorization>,
    // IOpenIddictScopeStore<MyScope>, IOpenIddictTokenStore<MyToken>,
    // ISigningKeyStore: GetValidKeysAsync, CreateKeyAsync, RetireKeyAsync, PurgeExpiredKeysAsync
    // (private key material must be encrypted at rest by your implementation)
}
```

> **Breaking change note**: `IHuiaStore` now also composes `IUserLoginStore<HuiaUser>` (`GetLoginsAsync`,
> `AddLoginAsync`, `RemoveLoginAsync`, `FindByLoginAsync`, `AddLoginAsync`'s siblings), which backs external
> (third-party) sign-in providers — see [external-providers.md](external-providers.md). An existing custom
> `IHuiaStore` implementation needs these members added even if you don't use external providers yet, since
> they're part of the interface unconditionally (the same way `ISigningKeyStore`'s members are).

> **Breaking change note**: `IHuiaStore` also composes `IHuiaPhoneNumberStore`
> (`FindByNormalizedPhoneNumberAsync`), which backs `huia.Authentication.UsePasswordlessFlow(...)` — see
> [passwordless.md](passwordless.md). An existing custom `IHuiaStore` implementation needs this member added
> even if you don't use passwordless sign-in, for the same reason as above.

> **Breaking change note**: `HuiaUser` is now abstract — every account is either a `PhoneUser` (phone-only,
> passwordless) or a `StandardUser` (email/password and/or external-login); `.WithEntityFrameworkStores<TContext>()`
> maps them to separate tables via EF Core's table-per-concrete-type (TPC) strategy. A custom, non-EF `IHuiaStore`
> implementation still programs against `HuiaUser` (`IUserStore<HuiaUser>` etc.) as the common reference type, but
> must decide for itself, from whatever storage it uses, which concrete leaf type to materialize for a given
> account — e.g. `CreateAsync(HuiaUser user, ...)` receives whichever concrete instance the caller constructed
> (`new PhoneUser {...}` or `new StandardUser {...}`), and `FindByIdAsync`/`FindByNormalizedPhoneNumberAsync`/etc.
> must return an instance of the correct leaf type for the account being looked up, not a bare `HuiaUser`
> (which can no longer be instantiated directly).

`WithStore<TStore, TApplication, TAuthorization, TScope, TToken>()` wires `MyStore` in as the Identity
user/role store, the OpenIddict application/authorization/scope/token store, and (since `IHuiaStore`
includes it) the `ISigningKeyStore` key management uses — and starts Quartz's hosted service (needed for
OpenIddict's own record pruning). Key management (automatic or manual) still needs to be enabled separately
via `huia.KeysManagement.Use*KeyManagement()`; `MyStore`'s `ISigningKeyStore` implementation is only used
once that's on.

## Admin list endpoint pagination

The admin list endpoints (`GET /api/identity/admin/{applications,scopes,authorizations,users}`) paginate
differently depending on which store you register — the response shape and query parameters aren't the same
across the two:

- **`.WithEntityFrameworkStores<TContext>()`**: real keyset pagination via
  [MR.AspNetCore.Pagination](https://github.com/mrahhal/MR.AspNetCore.Pagination), querying the EF Core
  entities directly. Page forward/backward with `after`/`before` query params — the id of the last/first item
  already on screen, not an opaque token — plus the existing `pageSize` param (clamped to 1–100, default 25).
  The response is MR's native shape:

  ```json
  { "data": [ ... ], "totalCount": 42, "pageSize": 25, "hasPrevious": false, "hasNext": true }
  ```

- **`.WithStore<TStore, ...>()`** (any custom, non-EF-Core store): the store-agnostic offset-based cursor
  scheme these endpoints have always used (see `Huia.Common.OffsetCursor`'s own doc comment for why it's
  offset-based internally despite the cursor-shaped API) — `cursor`/`pageSize` query params, and:

  ```json
  { "items": [ ... ], "nextCursor": "MjU=" }
  ```

This split exists because MR.AspNetCore.Pagination needs a live EF Core `IQueryable`, which a custom
`IHuiaStore` implementation can't offer without hard-coupling `IHuiaStore` itself to EF Core — the same
constraint `OffsetCursor`'s doc comment describes for why the fallback path stays offset-based rather than a
real keyset query. Each endpoint resolves an optional `IAdminEfCorePaginator` from DI (registered only by
`WithEntityFrameworkStores`) and falls through to the cursor-based path when it's absent, so a custom store
keeps working exactly as before — just without the native keyset shape above.

`GET /api/identity/admin/roles` is unaffected by this split: it's unpaginated by design regardless of store
(roles are typically few), always returning every role. Its response still mirrors the EF-Core shape's field
names (`data`/`totalCount`/`pageSize`/`hasPrevious`/`hasNext`, the last two always `false`) so a client can
treat all five admin list endpoints uniformly.
