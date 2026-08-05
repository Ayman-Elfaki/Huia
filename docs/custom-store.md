# Custom stores

`Huia.EntityFrameworkCore` is the default persistence provider, but it isn't required — Huia's persistence
is split into two independent, provider-agnostic extension points.

## `IHuiaStore` — Identity + OpenIddict data

`IHuiaStore<TApplication, TAuthorization, TScope, TToken>` composes ASP.NET Core Identity's user/role stores
with OpenIddict's application/authorization/scope/token stores into one interface. Implement it against
whatever you're using (Dapper, MongoDB, ...) and register it instead of
`.WithEntityFrameworkStores<TContext>()`:

```csharp
builder.Services.AddHuia(issuer, huia => { /* ... */ })
    .WithStore<MyStore, MyApplication, MyAuthorization, MyScope, MyToken>();

public class MyStore : IHuiaStore<MyApplication, MyAuthorization, MyScope, MyToken>
{
    // IUserStore<HuiaUser>, IRoleStore<HuiaRole>,
    // IOpenIddictApplicationStore<MyApplication>, IOpenIddictAuthorizationStore<MyAuthorization>,
    // IOpenIddictScopeStore<MyScope>, IOpenIddictTokenStore<MyToken>
}
```

`WithStore<TStore, TApplication, TAuthorization, TScope, TToken>()` wires `MyStore` in as both the Identity
user/role store and the OpenIddict application/authorization/scope/token store, and starts Quartz's hosted
service (needed for OpenIddict's own record pruning).

`IHuiaStore` says nothing about signing-key storage — that's the separate extension point below.

## `IHuiaSigningKeyStore` — signing/encryption keys

Needed only if you enable [key management](key-management.md) (automatic or manual). Implement this
directly for a dedicated backend (a cloud KMS, for instance), independent of however everything else is
stored:

```csharp
public interface IHuiaSigningKeyStore
{
    Task<IReadOnlyList<HuiaKeyDescriptor>> GetValidKeysAsync(HuiaKeyUsage usage, CancellationToken cancellationToken = default);
    Task<HuiaKeyDescriptor> CreateKeyAsync(HuiaKeyUsage usage, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
    Task RetireKeyAsync(string keyId, DateTimeOffset retiredAt, CancellationToken cancellationToken = default);
    Task PurgeExpiredKeysAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
```

Private key material must be encrypted at rest by your implementation. Register it with
`.WithSigningKeyStore<TKeyStore>()`:

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.KeysManagement.UseAutomaticKeyManagement();
    })
    .WithStore<MyStore, MyApplication, MyAuthorization, MyScope, MyToken>()
    .WithSigningKeyStore<MyKeyStore>();
```

`MyKeyStore` can be the same type as `MyStore` (if it also implements `IHuiaSigningKeyStore`) or a completely
separate type — signing-key storage doesn't have to live wherever the rest of your data lives.

## Mixing providers

Because the two extension points are independent, you can mix EF Core for one and a custom store for the
other — e.g. EF Core for Identity/OpenIddict data, but a cloud KMS for signing keys:

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.KeysManagement.UseAutomaticKeyManagement();
    })
    .WithEntityFrameworkStores<AppDbContext>()
    .WithSigningKeyStore<MyKmsKeyStore>();
```
