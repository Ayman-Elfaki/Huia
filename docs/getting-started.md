# Getting started

## Install

```bash
dotnet add package Huia
dotnet add package Huia.EntityFrameworkCore
```

## Wire it up

```csharp
using Huia;
using Huia.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=app.db"));

builder.Services.AddHuia("https://localhost:5001", huia =>
    {
        huia.Branding.Title = "My App";

        huia.Applications.AddSinglePageApplication(app =>
        {
            app.SetClientId("my-spa");
            app.AddRedirectUri("https://localhost:3000/callback");
            app.AddPostLogoutRedirectUri("https://localhost:3000");
            app.AllowScopes("my-api");
        });

        huia.KeysManagement.UseAutomaticKeyManagement();
    })
    .WithEntityFrameworkStores<AppDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseHuia();
app.UseAuthentication();
app.UseAuthorization();

app.MapHuiaConnectEndpoints();
app.MapHuiaManageEndpoints();
app.MapRazorPages();

app.Run();

public class AppDbContext(DbContextOptions<AppDbContext> options) : HuiaDbContext(options);
```

`issuer` (the first argument to `AddHuia`) is the URI OpenIddict advertises in tokens and discovery
metadata — it must match where the app is actually served.

## Registering client applications

`huia.Applications` declaratively registers the OAuth/OIDC clients Huia seeds into the OpenIddict store on
startup (idempotently — safe to run every time the app starts):

| Method | Client type | Flow | Holds a secret? |
|---|---|---|---|
| `AddSinglePageApplication` | Public, browser-based (React/Vue/etc.) | Authorization code + PKCE | No |
| `AddNativeApplication` | Public, installed app | Authorization code + PKCE | No |
| `AddServerSideWebApplication` | Confidential, server-rendered web app | Authorization code (+ optional PKCE) | Yes |
| `AddMachine2Machine` | Confidential, non-interactive | Client credentials | Yes |
| `AddDevice` | Public, input-constrained device | Device authorization | No |

Every registration needs at least `SetClientId(...)`; confidential clients also need
`SetClientSecret(...)`. `AllowScopes(...)` grants the scopes that client is permitted to request — including
`profile`/`email`/`roles` if you want those claims in the tokens it gets, since only `openid` (and
`offline_access`, where applicable) are granted automatically.

A device application's own approval step (a signed-in user confirming a pending device code at
`/connect/verify` — see [`Huia.Cli`](cli.md), which authenticates this way) needs
`app.UseAntiforgery()` added to the pipeline after `app.UseAuthentication()`/`app.UseAuthorization()` and
before `MapHuiaConnectEndpoints()`, the same way those two are wired explicitly rather than bundled into
`app.UseHuia()`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHuiaConnectEndpoints();
```

Without it, approving a device code throws — the POST is form-bound (OpenIddict's own device/verification
endpoints only accept `application/x-www-form-urlencoded`, unlike Huia's other JSON endpoints), and ASP.NET
Core requires antiforgery validation on any form-bound minimal API parameter automatically. That's
intentional, not incidental: without it, any page could silently submit an approval on a signed-in user's
behalf for a device code the page's own author generated.

### Per-client token lifetimes

By default, every client gets the same access/identity/refresh token lifetimes (OpenIddict's own server-wide
defaults). Override them for a specific client with `SetAccessTokenLifetime`, `SetIdentityTokenLifetime`, and
`SetRefreshTokenLifetime`:

```csharp
huia.Applications.AddSinglePageApplication(app =>
{
    app.SetClientId("my-spa");
    // ...
    app.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
    app.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
});
```

Leave unset to fall back to the server-wide lifetime. The same three fields are also settable per client
through the admin `/applications` API (and `huia applications create/update`, see [cli.md](cli.md)) — useful
for tightening access tokens on a high-security client or extending refresh tokens for a trusted native app,
without changing the lifetime for every other client.

## Key management

Signing/encryption keys need somewhere to live. Call one of these inside the `AddHuia(...)` configuration
callback, after `.WithEntityFrameworkStores<TContext>()` has been chained (or before — order between the two
doesn't matter):

- `huia.KeysManagement.UseAutomaticKeyManagement()` — rotates keys on a schedule with a grace period for
  already-issued tokens. Recommended for most apps.
- `huia.KeysManagement.UseManualKeyManagement()` — you decide when keys are created/retired via
  `HuiaKeyManager`.

See [key-management.md](key-management.md) for the full policy knobs.

## Mapping endpoints

- `app.MapHuiaConnectEndpoints()` — the OpenIddict endpoints: `/connect/authorize`, `/connect/token`,
  `/connect/userinfo`, `/connect/logout`, `/connect/device`, `/connect/verify`.
- `app.MapHuiaManageEndpoints()` — JSON endpoints a signed-in user calls to manage their own account
  (`/identity/manage/api/2fa`, `/identity/manage/api/info`).
- `app.MapHuiaAdminEndpoints()` — JSON CRUD over applications, scopes, live authorizations, users, and roles
  at `/identity/admin/api/*`. **Not authorized by default** — chain `.RequireAuthorization(...)` onto the
  returned `RouteGroupBuilder` yourself. [`Huia.Cli`](cli.md) is a terminal client for this API.
- `app.MapRazorPages()` — Huia's own server-rendered Identity pages (login, register, 2FA, password reset,
  device code approval, ...) live under `/identity/account/*` as Razor Pages bundled with the `Huia` package
  itself. The device page (`/identity/account/device`) is what `verification_uri` points a real user's
  browser at by default for the [device authorization flow](cli.md) — `/connect/verify` stays mapped
  alongside it as a secondary URI exposing the same approval step as JSON, for a client that wants to render
  its own UI instead.

## Logout

`/connect/logout` (RP-initiated logout) always signs the user out at Huia and, for the client that sent them
there, redirects back to its `post_logout_redirect_uri` — see `AddPostLogoutRedirectUri` above. If the same
user also has live SSO sessions with *other* clients, register those clients' front/back-channel logout
URIs (per the OpenID Connect Front-Channel/Back-Channel Logout 1.0 specs) so they get torn down too:

```csharp
app.SetFrontChannelLogoutUri("https://other-app.example/oidc/frontchannel-logout");
app.SetBackChannelLogoutUri("https://other-app.example/oidc/backchannel-logout");
```

- **Front-channel**: Huia serves a brief HTML page with a hidden `<iframe>` per registered
  `FrontChannelLogoutUri` (each with an `?iss=` query parameter) before continuing on to the usual
  redirect — the browser has to actually be there for these to load.
- **Back-channel**: Huia POSTs a signed `logout_token` directly to each registered `BackChannelLogoutUri`,
  server-to-server, no browser involved. A slow or unreachable client here never blocks or fails the user's
  own sign-out.

Both are keyed by the signed-out **session** (`sid`), not the subject — only client applications with a live
authorization tied to *that specific session* are notified; a different session for the same user (a
different browser or device) is untouched. The client that itself initiated the logout is never notified of
its own logout.

## Home URL

```csharp
app.SetHomeUri("https://localhost:3000");
```

Where to send the browser back to this client when there's no in-flight OAuth request to resume. Two places
fall back to it: an email confirmation/password reset link
clicked out of band, after whatever originally triggered it has already completed or gone stale (replaying
the original `/connect/authorize` URL would fail the client's PKCE check instead); and the branded error
page's "return home" link, when a failing request (e.g. an unregistered `redirect_uri`) still names a real
`client_id`. Only the client itself can start a new, correctly-signed OAuth request — landing back on it,
where its own "Sign in" does that, is as far as Huia can safely take the user automatically. Without a
`HomeUri`, both fall back to the origin of one of the client's registered redirect URIs instead.

## Sending real email

By default, Huia logs confirmation/password-reset links instead of sending them
(`NoOpEmailSender`). Register your own `IEmailSender<HuiaUser>` **before** calling `AddHuia(...)` to actually
deliver mail — `HuiaEmailTemplate` (registered by `AddHuia`) renders the branded HTML body for you:

```csharp
public sealed class SmtpEmailSender(HuiaEmailTemplate template) : IEmailSender<HuiaUser>
{
    public Task SendConfirmationLinkAsync(HuiaUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirm your email", template.ConfirmationLink(confirmationLink));

    // ...
}

builder.Services.AddSingleton<IEmailSender<HuiaUser>, SmtpEmailSender>(); // before AddHuia
```

See [samples/Huia.TodoApi/Email/SmtpEmailSender.cs](../samples/Huia.TodoApi/Email/SmtpEmailSender.cs)
for a complete example that sends over SMTP to [Mailpit](https://mailpit.axllent.org/).

## Next steps

- [Architecture](architecture.md) — how the pieces fit together.
- [Localization](localization.md) — English/Arabic out of the box, add more.
- [Custom stores](custom-store.md) — back persistence with something other than EF Core.
- [Samples](samples.md) — a complete Todo CRUD API + Next.js frontend, run with Aspire.
