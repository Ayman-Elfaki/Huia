# External providers

Huia can let a user sign in through a third-party identity provider (Google, Microsoft, GitHub, or any
OAuth2/OIDC provider) instead of — or alongside — a Huia password. This builds on ASP.NET Core Identity's
own external-login mechanism (`SignInManager<HuiaUser>`), the same one the classic scaffolded Identity UI
uses, so any standard ASP.NET Core remote-authentication handler works unmodified.

## Registering a provider

Register providers inside the `AddHuia(issuer, huia => {...})` callback, via
`huia.Authentication.UseExternalAuthenticationFlow(ext => {...})` — `ext.Providers` is the same
`AuthenticationBuilder` `AddHuia` itself uses internally, exposed directly rather than wrapped:

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.Authentication.UseExternalAuthenticationFlow(ext =>
        {
            ext.Providers.AddGoogle(google =>
            {
                google.ClientId = builder.Configuration["Google:ClientId"]!;
                google.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
            });
        });
    })
    .WithEntityFrameworkStores<AppDbContext>();
```

`AddGoogle`/`AddMicrosoftAccount`/`AddOpenIdConnect` aren't part of the ASP.NET Core shared framework —
each needs its own NuGet package:

```bash
dotnet add package Microsoft.AspNetCore.Authentication.Google
```

Only the generic `AddOAuth` (for a provider without a dedicated package — GitHub, for instance, via
[`AspNet.Security.OAuth.GitHub`](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers), or any
other OAuth2 provider) and `AddOpenIdConnect` ship with ASP.NET Core itself. Huia doesn't special-case any
specific provider by name — register as many as you like, using whichever handler fits.

You don't need to set `SignInScheme` on a provider — `AddHuia` already defaults
`AuthenticationOptions.DefaultSignInScheme` to `IdentityConstants.ExternalScheme`, the scheme
`SignInManager<HuiaUser>.GetExternalLoginInfoAsync()` reads from, so every remote handler lands there the
same way it would under plain ASP.NET Core Identity.

For a generic `AddOAuth` provider (no dedicated package), you also need to fetch the user's profile
yourself and set `Events.OnRemoteFailure` so a provider-side error (the user denying consent, etc.) redirects
back into Huia's sign-in flow instead of throwing:

```csharp
huia.Authentication.UseExternalAuthenticationFlow(ext => ext.Providers.AddOAuth("github", "GitHub", oauth =>
{
    oauth.ClientId = builder.Configuration["GitHub:ClientId"]!;
    oauth.ClientSecret = builder.Configuration["GitHub:ClientSecret"]!;
    oauth.CallbackPath = "/signin-github";
    oauth.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
    oauth.TokenEndpoint = "https://github.com/login/oauth/access_token";
    oauth.UserInformationEndpoint = "https://api.github.com/user";

    oauth.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
    oauth.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "name");

    oauth.Events.OnCreatingTicket = async context =>
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

        using var response = await context.Backchannel.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();

        using var user = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        context.RunClaimActions(user.RootElement);
    };

    oauth.Events.OnRemoteFailure = context =>
    {
        context.Response.Redirect($"/identity/account/externallogin?handler=Callback&remoteError={Uri.EscapeDataString(context.Failure?.Message ?? "unknown_error")}");
        context.HandleResponse();
        return Task.CompletedTask;
    };
}));
```

`AddGoogle`/`AddMicrosoftAccount`/`AddOpenIdConnect` already do the profile fetch and a sensible
`OnRemoteFailure` for you.

## Sign-in

Once at least one provider is registered, `/identity/account/login` automatically lists a button per
provider — `LoginModel` reads them via `SignInManager<HuiaUser>.GetExternalAuthenticationSchemesAsync()`, so
there's nothing extra to wire up on the UI side.

Clicking a provider button posts to `/identity/account/externallogin`, which challenges the provider and
completes at its callback:

- Already linked to a local account → signed in directly (2FA and lockout are honored exactly like a
  password sign-in — an account with 2FA enabled is routed through the existing `LoginWith2fa` page).
- Not linked yet, and the provider's email doesn't match an existing account → if the provider reported
  `email_verified: true` and supplied both a given and family name, the account is created and signed in
  immediately, no extra step — reliable for Google/Microsoft, not guaranteed for a generic `AddOAuth`
  registration (see `ExternalClaimsMapper`). Anything less complete is redirected to
  `ExternalLoginConfirmation` instead, pre-filled from whatever claims the provider did supply, for explicit
  consent (and to collect what's missing) before the account is created; a field the provider already
  supplied is shown read-only there rather than editable. If the provider's email wasn't verified, Huia sends
  the normal confirmation email instead of signing in immediately.
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
            ext.Providers.AddGoogle(google => { /* ... */ });
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
