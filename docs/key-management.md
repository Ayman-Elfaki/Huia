# Key management

Huia can manage its own signing/encryption keys — generating new ones ahead of time, retiring old ones on a
schedule, and keeping retired keys around long enough to validate tokens they already issued. Configure it
inside the `AddHuia(issuer, huia => {...})` callback, via `huia.KeysManagement`.

## Automatic key management

A new key is generated ahead of the current one's retirement so there's never a gap, and retired keys stay
valid for validating already-issued tokens for a grace period before being purged.

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.KeysManagement.UseAutomaticKeyManagement(options =>
        {
            options.ActiveLifetime = TimeSpan.FromDays(90);      // how long a key signs new tokens
            options.RotationLeadTime = TimeSpan.FromDays(14);    // generate the next key this far ahead
            options.ValidationOverlap = TimeSpan.FromDays(30);   // how long a retired key still validates
            options.RotationCronExpression = "0 0 3 * * ?";      // when the rotation check runs (Quartz cron)
            options.RsaKeySizeInBits = 2048;
            options.SigningAlgorithm = HuiaSigningAlgorithm.RS256; // RS256/RS384/RS512 or PS256/PS384/PS512
        });
    })
    .WithEntityFrameworkStores<AppDbContext>();
```

`ValidationOverlap` should be at least as long as your longest-lived token — typically the refresh token's
lifetime, since a refresh token issued just before rotation still needs the old key to validate the access
tokens it's exchanged for afterward.

`SigningAlgorithm` only affects newly-created signing keys — each key's algorithm is carried in its own
JWKS/JWT header, so changing it doesn't require rotating out existing keys, and a key created under the old
setting keeps validating under it for the rest of its validation overlap. It has no effect on the encryption
key, which always uses RSA-OAEP with A256CBC-HS512.

The rotation check runs as a [Quartz](https://www.quartz-scheduler.net/) job.
`WithEntityFrameworkStores`/`WithStore` already start Quartz's hosted service (for OpenIddict's own record
pruning), so no extra wiring is needed there. If you're backing key storage with a non-EF-Core
`IHuiaSigningKeyStore` directly (bypassing `WithStore`), call `services.AddQuartzHostedService()` yourself so
the rotation job actually runs.

## Manual key management

You decide when a key is created or retired — from an admin action, a CLI command, your own schedule,
whatever fits. No Quartz job is registered.

```csharp
huia.KeysManagement.UseManualKeyManagement();
```

```csharp
// Somewhere in your own code:
var keyManager = serviceProvider.GetRequiredService<HuiaKeyManager>();
await keyManager.CreateKeyAsync(HuiaKeyUsage.Signing, expiresAt: DateTimeOffset.UtcNow.AddDays(90));
await keyManager.RetireKeyAsync(oldKeyId, retiredAt: DateTimeOffset.UtcNow);
```

## Storage

Either mode needs an `ISigningKeyStore` registered — chain one of these onto the builder `AddHuia(...)`
returns (order relative to `huia.KeysManagement.Use*KeyManagement()` doesn't matter):

- `.WithEntityFrameworkStores<TContext>()` (from `Huia.EntityFrameworkCore`) — also backs Identity/OpenIddict
  persistence.
- `.WithStore<TStore, ...>()` — `TStore` implements `ISigningKeyStore` as part of `IHuiaStore`, so this
  registers it automatically.
- A custom `ISigningKeyStore` implementation registered directly (e.g. `services.AddScoped<ISigningKeyStore,
  MyKmsKeyStore>()`), independent of everything else — a cloud KMS, for instance, while Identity/OpenIddict
  data stays on EF Core or a custom `IHuiaStore`.

Private key material is encrypted at rest by the store implementation — `Huia.EntityFrameworkCore`'s uses
[ASP.NET Core Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/introduction).

## Disabling access token encryption

By default, OpenIddict issues access tokens as encrypted JWEs. If a separate resource server needs to inspect
an access token's claims directly — without sharing Huia's encryption key — call
`huia.Server.DisableAccessTokenEncryption()`. Signing alone still guarantees integrity and authenticity; the
resource server validates the signature against Huia's public JWKS (`/.well-known/jwks`) without needing any
shared secret.
