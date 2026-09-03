# External login

Federated sign-in ("sign in with Google / a partner IdP") is implemented **entirely through the
OpenIddict client** — never the classic ASP.NET Core authentication handlers. A tenant enables it by
registering at least one provider.

## Configuration

```csharp
tenant.Authentication.UsePasswordlessFlow(pwl => pwl.UseExternalLogin(ext =>
{
    ext.AddGoogle("client-id", "client-secret");
    ext.AddGitHub("client-id", "client-secret");
    ext.AddMicrosoftAccount("client-id", "client-secret");
    ext.AddOpenIdConnect("Partner", "client-id", "client-secret", "https://partner.example/", p =>
    {
        p.DisplayName = "Partner";
        p.Scopes.Add("profile");
        p.Scopes.Add("email");
    });

    // Opt-in: a logged-out external sign-in whose verified email matches an existing local account
    // links to it instead of starting a new sign-up (see the guard below).
    ext.EnableAccountsLinking();
}));
```

Each `(tenant, provider)` becomes one `OpenIddictClientRegistration` with
`RegistrationId = "{tenant}:{name}"` and a `/signin-{name}` callback path. The provider name is used
in the registration id and the callback path, so keep it stable. `IsExternalLoginEnabled` flips true
once a provider is registered.

## Routes

| Route | Purpose |
|---|---|
| `POST identity/account/external/{provider}` | starts the OpenIddict-client challenge (antiforgery-protected) |
| `signin-{provider}` | the provider callback (passthrough) |
| `identity/account/externallogincallback` | the dispatcher — decides link / sign-in / provision |
| `signout-callback-oidc` | RP-initiated end-session at the upstream provider on local sign-out |

## Dispatcher

`ExternalLoginCallbackAsync` handles four cases:

1. **A signed-in user is linking** (from `identity/account/externallogins`) — attach the login to the
   current account, or report `ok` / `dupe` / `error`.
2. **Already linked** — sign the matched account in (`amr` = provider name, `idp` + id-token claims).
3. **Logged out, email matches a local account** —
   `HuiaUserManager.TryLinkExternalByEmailAsync(email, providerVouches, accountLinkingEnabled, …)`:
   - `Linked` — linking is on **and** the local email is confirmed **and** the provider vouches
     (`email_verified != "false"`) → sign in.
   - `Blocked` — otherwise → refuse (`?linkError=1`), never create a duplicate.
4. **First time, no email match** — hand off to `completeprofile`, which calls
   `HuiaUserManager.CreateExternalUserAsync` (derive username from the email or a
   `slug(displayName)-{key}`, create, attach the login).

Unlinking is guarded by `HuiaUserManager.CanRemoveExternalLoginAsync` — refused when it would leave
the account with no password, no phone and one or zero logins.

## Security headers

When `AddHuiaSecurityHeaders()` is on, each external provider's `Authority` origin is added to the
CSP `form-action` directive so the challenge POST is not blocked.

## The `Huia.External` sample

`samples/Huia.External` is a second Huia instance acting as the upstream "partner" IdP for the `todo`
tenant's external-login button. `tests/Huia.E2ETests/FrontEndAuthE2ETests.cs` drives the full
round-trip (including the `EnableAccountsLinking()` path) through the Nuxt Todo.App.
