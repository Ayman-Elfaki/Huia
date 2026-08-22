# Custom stores

`Huia.EntityFrameworkCore` is the default persistence provider, but it isn't required — Huia's persistence
is provider-agnostic.

## `IHuiaStore` — user/role, OpenIddict, and key data

`IHuiaStore<TApplication, TAuthorization, TScope, TToken>` composes Huia's own user/role/login/token store
interfaces, OpenIddict's application/authorization/scope/token stores, and `ISigningKeyStore` (signing/
encryption key storage) into one interface. Implement it against whatever you're using (Dapper, MongoDB,
...) and register it instead of `.WithEntityFrameworkStores<TContext>()`:

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.KeysManagement.UseAutomaticKeyManagement();
    })
    .WithStore<MyStore, MyApplication, MyAuthorization, MyScope, MyToken>();

public class MyStore : IHuiaStore<MyApplication, MyAuthorization, MyScope, MyToken>
{
    // IHuiaUserStore: FindByIdAsync, FindByNormalizedUserNameAsync, FindByNormalizedEmailAsync,
    //   CreateAsync, UpdateAsync, DeleteAsync, Users
    // IHuiaUserLoginStore: AddLoginAsync, RemoveLoginAsync, GetLoginsAsync, FindByLoginAsync
    // IHuiaUserRoleStore: AddToRoleAsync, RemoveFromRoleAsync, GetRolesAsync, IsInRoleAsync, GetUsersInRoleAsync
    // IHuiaUserTokenStore: SetTokenAsync, GetTokenAsync
    // IHuiaPhoneNumberStore: FindByNormalizedPhoneNumberAsync
    // IHuiaRoleStore: FindByIdAsync, FindByNormalizedNameAsync, CreateAsync, UpdateAsync, DeleteAsync, Roles
    // IOpenIddictApplicationStore<MyApplication>, IOpenIddictAuthorizationStore<MyAuthorization>,
    // IOpenIddictScopeStore<MyScope>, IOpenIddictTokenStore<MyToken>,
    // ISigningKeyStore: GetValidKeysAsync, CreateKeyAsync, RetireKeyAsync, PurgeExpiredKeysAsync
    // (private key material must be encrypted at rest by your implementation)
}
```

`UpdateAsync` (on both `IHuiaUserStore` and `IHuiaRoleStore`) must throw `HuiaConcurrencyException` if the
persisted row's `ConcurrencyStamp` no longer matches the value on the entity passed in — `HuiaUserManager`/
`HuiaRoleManager` translate that into a `ConcurrencyFailure` error instead of silently overwriting a
concurrent change.

> **Breaking change note** (third in a row for this interface — see the changelog at the bottom of this
> file): `IHuiaStore` no longer composes ASP.NET Core Identity's `IUserStore<HuiaUser>`/
> `IUserLoginStore<HuiaUser>`/`IRoleStore<HuiaRole>` — Huia dropped its dependency on
> `Microsoft.AspNetCore.Identity` entirely in favor of its own `HuiaUser`/`HuiaRole` model and manager
> equivalents (`HuiaUserManager`, `HuiaSignInManager`, `HuiaRoleManager`). An existing custom `IHuiaStore`
> implementation needs to be re-targeted at the `IHuiaUserStore`/`IHuiaUserLoginStore`/`IHuiaUserRoleStore`/
> `IHuiaUserTokenStore`/`IHuiaRoleStore` interfaces listed above — the shapes are similar (same
> id/normalized-name lookups, same login/role composition) but the types are Huia's own, not Identity's.
> `IHuiaUserTokenStore` is new: a single small (user, provider, name) → value contract that collapses what
> used to be three separate Identity interfaces (`IUserTokenStore`, `IUserAuthenticationTokenStore`,
> `IUserTwoFactorRecoveryCodeStore`) — it backs password-reset/email-confirmation/phone-OTP tokens, the TOTP
> `AuthenticatorKey`, and hashed 2FA recovery codes.

> **Breaking change note**: `IHuiaStore` also composes `IHuiaPhoneNumberStore`
> (`FindByNormalizedPhoneNumberAsync`), which backs `huia.Authentication.UsePasswordlessFlow(...)` — see
> [passwordless.md](passwordless.md). An existing custom `IHuiaStore` implementation needs this member added
> even if you don't use passwordless sign-in, for the same reason as above.

`WithStore<TStore, TApplication, TAuthorization, TScope, TToken>()` wires `MyStore` in as the user/role/
login/token store, the OpenIddict application/authorization/scope/token store, and (since `IHuiaStore`
includes it) the `ISigningKeyStore` key management uses — and starts Quartz's hosted service (needed for
OpenIddict's own record pruning). Key management (automatic or manual) still needs to be enabled separately
via `huia.KeysManagement.Use*KeyManagement()`; `MyStore`'s `ISigningKeyStore` implementation is only used
once that's on.

## Password hashing and tokens are no longer part of the store

Password hashing (`IHuiaPasswordHasher`, defaulting to `Pbkdf2PasswordHasher` — PBKDF2-HMACSHA256, no
external crypto package) and token generation/validation (`IHuiaTokenProvider`, with three built-in
providers — `DataProtectorHuiaTokenProvider` for password-reset/email-confirmation/change-phone-number,
`PhoneOtpHuiaTokenProvider` for phone/SMS OTP, `TotpHuiaTokenProvider` for authenticator-app 2FA) are
separate, independently pluggable services rather than store responsibilities. Register your own
`IHuiaPasswordHasher`/additional `IHuiaTokenProvider` implementations in DI after `AddHuia` to override or
extend them — a custom `IHuiaStore` only needs to persist whatever these produce via `IHuiaUserTokenStore`,
not implement the algorithms itself.

## Changelog

- **This change**: `IHuiaStore` dropped `Microsoft.AspNetCore.Identity` entirely — see the breaking-change
  note above. `HuiaUser`/`HuiaRole` no longer inherit `IdentityUser`/`IdentityRole`; `UserManager<HuiaUser>`/
  `SignInManager<HuiaUser>`/`RoleManager<HuiaRole>` are replaced by `HuiaUserManager`/`HuiaSignInManager`/
  `HuiaRoleManager`.
- **Previous change**: `IHuiaStore` gained `IUserLoginStore<HuiaUser>` (external logins) and
  `IHuiaPhoneNumberStore` (passwordless phone sign-in).

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
