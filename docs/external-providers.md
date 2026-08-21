# External providers

Huia can let a user sign in through a third-party identity provider (Google, Microsoft, GitHub, or any
OAuth2/OIDC provider) instead of — or alongside — a Huia password. This is built on OpenIddict's own client
stack (`OpenIddict.Client`/`OpenIddict.Client.WebIntegration`, the same package family Huia's own
authorization server is built on) rather than ASP.NET Core Identity's generic remote-authentication handlers —
but the resulting sign-in still flows through `SignInManager<HuiaUser>` exactly the way it always has, so
everything downstream of "a provider redirected back" (account linking, auto-provisioning, 2FA/lockout
routing, email-collision handling) is unaffected by which mechanism completed the handshake.

## Registering a provider

Register providers inside the `AddHuia(issuer, huia => {...})` callback, via
`huia.Authentication.UseExternalAuthenticationFlow(ext => {...})`:

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.Authentication.UseExternalAuthenticationFlow(ext =>
        {
            ext.WebProviders.AddGoogle(google => google
                .SetClientId(builder.Configuration["Google:ClientId"]!)
                .SetClientSecret(builder.Configuration["Google:ClientSecret"]!)
                .SetRedirectUri("callback/login/google"));
        });
    })
    .WithEntityFrameworkStores<AppDbContext>();
```

`ext.WebProviders` (an `OpenIddictClientWebIntegrationBuilder`, from the `OpenIddict.Client.WebIntegration`
package) ships pre-configured settings for Google, Microsoft, GitHub, and 100+ other services — no need to
look up authorization/token endpoints yourself. Every registration needs its own `SetRedirectUri(...)`, which
must match the route Huia's own callback bridge is mapped at (see [Wiring it up](#wiring-it-up) below):
`callback/login/{a name unique to this provider}` — `callback/login/google`, `callback/login/github`, and so
on if you register more than one.

For a provider without a named `WebProviders` integration — most often a custom-issuer OIDC provider, like a
second Huia instance — use `ext.Client.AddRegistration(...)` instead, the raw `OpenIddict.Client` API:

```csharp
huia.Authentication.UseExternalAuthenticationFlow(ext => ext.Client.AddRegistration(new OpenIddictClientRegistration
{
    Issuer = new Uri("https://idp.example/"),
    ClientId = builder.Configuration["ExternalIdp:ClientId"]!,
    ClientSecret = builder.Configuration["ExternalIdp:ClientSecret"],
    ProviderName = "example-idp",
    ProviderDisplayName = "Example IdP",
    RedirectUri = new Uri("callback/login/example-idp", UriKind.Relative),
    // Unlike a named ext.WebProviders integration, a generic registration has to list every scope it needs
    // explicitly — including "openid" itself.
    Scopes = { OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Email },
}));
```

**Runnable example**: `samples/Huia.IdentityServer` is a second, independent Huia instance playing the role
of a third-party identity provider — registered on `Huia.TodoApi` as `huia-idp` via `ext.Client.AddRegistration(...)`
(see `Huia.TodoApi/Program.cs`). Because it's Huia on both ends, it's a real, controllable external IdP that an
automated test can actually sign in to (unlike a real Google/Microsoft account) — see
`tests/Huia.Tests.E2E/ExternalIdentityServerLoginE2ETests.cs`, which drives the whole challenge → external
sign-in → callback → account-creation round trip against it in a real browser. The same sample is registered a
second time as `huia-idp-partial`, requesting no `profile` scope, to demonstrate and test the other branch: a
provider that doesn't supply every profile claim (a generic registration isn't guaranteed to) routes to
`ExternalLoginConfirmation` with editable, blank name fields instead of auto-provisioning — the same test
file's second `[Fact]` drives that path too.

## Wiring it up

OpenIddict's client stack completes the OAuth2/OIDC handshake itself but, unlike the ASP.NET Core remote-auth
handlers Huia used to build this on, doesn't sign the result into any cookie on its own. Huia bridges that gap
with its own endpoint — map it alongside `MapRazorPages()`:

```csharp
app.MapRazorPages();
app.MapHuiaExternalLoginCallbackEndpoints();
```

This is what every provider's `RedirectUri`/`SetRedirectUri(...)` must point at (`callback/login/{provider}`).
It reads OpenIddict's own authentication result and signs it into `IdentityConstants.ExternalScheme` — the
same cookie a remote-authentication handler used to populate directly — so `SignInManager<HuiaUser>`'s
external-login methods keep working exactly as before. There's nothing else to wire up: no `SignInScheme` to
set, no `CallbackPath` to reserve.

## Sign-in

Once at least one provider is registered, `/identity/account/login` automatically lists a button per
provider — `LoginModel` reads them via `SignInManager<HuiaUser>.GetExternalAuthenticationSchemesAsync()`
(each registration's provider name is automatically forwarded as its own authentication scheme), so there's
nothing extra to wire up on the UI side.

Clicking a provider button posts to `/identity/account/externallogin`, which challenges the provider; the
provider redirects back to Huia's callback bridge, which hands off into the same page's callback handler:

- Already linked to a local account → signed in directly (2FA and lockout are honored exactly like a
  password sign-in — an account with 2FA enabled is routed through the existing `LoginWith2fa` page).
- Not linked yet, and the provider's email doesn't match an existing account → if the provider reported
  `email_verified: true` and supplied both a given and family name, the account is created and signed in
  immediately, no extra step — reliable for Google/Microsoft, not guaranteed for a generic registration (see
  `ExternalClaimsMapper`). Anything less complete is redirected to `ExternalLoginConfirmation` instead,
  pre-filled from whatever claims the provider did supply, for explicit consent (and to collect what's
  missing) before the account is created; a field the provider already supplied is shown read-only there
  rather than editable. If the provider's email wasn't verified, Huia sends the normal confirmation email
  instead of signing in immediately.
- Not linked yet, but the provider's email **does** match an existing (password) account → Huia does **not**
  auto-link it. By default, the user has to sign in with their password first, then link the provider from
  account settings (below); with `ext.EnablePasswordLinking()` (see below), they can instead link it right
  there by entering that account's password.
- The provider returns an error, or the callback can't be completed → back to `Login` with an explanatory
  message.

If the provider reports a `picture` claim, it's stored on `HuiaUser.Picture` when the account is created —
see [Avatars](#avatars) below.

## Password-confirmed linking on email collision

By default, an external sign-in whose email collides with an existing password account is rejected outright
(the bullet above) — a forged or compromised external claim shouldn't silently gain access to an existing
account on the strength of an unverified email match alone. `ext.EnablePasswordLinking()` opts into a middle
ground: instead of rejecting it, Huia asks the user to type that account's password. Getting it right proves
ownership — the same bar a signed-in "link from settings" flow already assumes — and links the external
identity to it right there, going through the same lockout tracking and 2FA routing as a normal password
sign-in:

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.Authentication.UseExternalAuthenticationFlow(ext =>
        {
            ext.WebProviders.AddGoogle(google => { /* ... */ });
            ext.EnablePasswordLinking();
        });
    })
    .WithEntityFrameworkStores<AppDbContext>();
```

Off by default. Turning it on only changes convenience, not the actual security boundary — a compromised
password already grants full account access regardless of this setting.

## Avatars

`HuiaUser.Picture` is populated from the provider's `picture` claim (Google and Microsoft both send one) when
an account is created through external sign-in — never for a password-registered account, and never
re-fetched or validated afterward, so treat it as untrusted display data. It's included in the identity/access
token under the `profile` scope (alongside `given_name`/`family_name`), in `GET /api/identity/manage/info`
(and settable there too — an empty string clears it), and in the admin `GET /api/identity/admin/users`
response.

## Managing linked providers

A signed-in user manages their own linked providers through `/api/identity/manage/external-logins`:

| Method | Route | |
|---|---|---|
| `GET` | `/api/identity/manage/external-logins` | Lists linked providers, plus whether the account has a password. |
| `DELETE` | `/api/identity/manage/external-logins/{provider}/{providerKey}` | Unlinks one. Rejected with 400 if it's the account's only sign-in method (no password, no other linked provider) — removing it would lock the user out. |
| `GET` | `/api/identity/manage/external-logins/{provider}/link?returnUrl=...` | Starts linking a new provider to the signed-in account — a real browser redirect through the provider, not a JSON call, the same reason sign-in itself is a page rather than an API. |
| `GET` | `/api/identity/manage/external-logins/{provider}/link-callback` | Completes it and redirects to `returnUrl`. |

Linking stashes the signed-in user's id as the challenge's xsrf key and re-checks it on the way back —
without that, an attacker who starts their own link flow and gets a victim to open the resulting callback
URL could bind their external identity to the victim's account instead of their own.

## Admin visibility

`GET /api/identity/admin/users` and `GET /api/identity/admin/users/{id}` (see the
[admin API](../README.md#endpoints)) include each user's `Picture`, `HasPassword`, and `ExternalLogins`
(`loginProvider`/`providerDisplayName` per linked provider) — the same data
`/api/identity/manage/external-logins` exposes for self-service, surfaced for an admin looking at someone
else's account.

## Custom stores

`IHuiaStore<TApplication, TAuthorization, TScope, TToken>` composes `IUserLoginStore<HuiaUser>` — a fully
custom (non-EF-Core) store needs to implement it to support external providers. See
[custom-store.md](custom-store.md).
