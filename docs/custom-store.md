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

`WithStore<TStore, TApplication, TAuthorization, TScope, TToken>()` wires `MyStore` in as the Identity
user/role store, the OpenIddict application/authorization/scope/token store, and (since `IHuiaStore`
includes it) the `ISigningKeyStore` key management uses — and starts Quartz's hosted service (needed for
OpenIddict's own record pruning). Key management (automatic or manual) still needs to be enabled separately
via `huia.KeysManagement.Use*KeyManagement()`; `MyStore`'s `ISigningKeyStore` implementation is only used
once that's on.
