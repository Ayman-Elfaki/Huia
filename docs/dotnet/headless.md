# `Huia.Headless`

`Huia.Headless` is the second authentication flavor: a **single-tenant**, bearer-token identity API
for apps that own their own login form and don't need OIDC redirects or multi-tenancy. It reuses the
same core `Huia` building blocks as `Huia.OpenId` (`HuiaUserManager<TUser>`, `HuiaSignInManager<TUser>`,
`HuiaPasskeyRegistrar<TUser>`) but has no dependency on OpenIddict or Finbuckle, and no Razor account
UI — every route returns JSON.

Package split: `Huia.Headless.EntityFrameworkCore` provides a plain
`HuiaDbContext : IdentityDbContext<HuiaUser, HuiaRole, string>` (no multi-tenant schema additions);
`Huia.Headless` provides `AddHuiaHeadless()` and `MapHuiaHeadlessEndpoints()`.

## Wiring it up

```csharp
builder.Services.AddDbContext<HuiaDbContext>(o => o.UseSqlite(connectionString));

builder.Services.AddHuia(huia =>
{
    huia.UseIssuer("https://api.example.com");
    huia.AddTenant("shop", tenant =>
    {
        tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
    });
})
    .AddEntityFrameworkCoreStores<HuiaDbContext>()
    .AddHuiaHeadless();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHuiaHeadlessEndpoints();
```

`AddHuiaHeadless()` throws `InvalidOperationException` if the options tree configures anything other
than **exactly one** tenant — this is enforced, not just a convention. There is no `UseHuiaHeadless()`
middleware helper and no `AddHuiaOpenId()`-style multi-tenancy wiring; a Headless host is a normal
single-tenant ASP.NET Core app, so it calls the framework's own `UseAuthentication()`/`UseAuthorization()`
directly.

## Authentication scheme

Registered via `AddAuthentication(IdentityConstants.BearerScheme).AddBearerToken(...)` — ASP.NET
Core Identity's stock bearer tokens (Data-Protection-wrapped, **opaque**, not JWTs). This means:

- A resource server can only validate tokens it itself issued — there is no JWKS/discovery endpoint
  to hand a token to a separate API, unlike `Huia.OpenId`'s OpenIddict-issued JWTs.
- Tokens are **not revocable** individually; `Huia.Headless` has no signing-key rotation or
  key-lifecycle jobs (there's no JWT to sign).
- A client's only sign-out action is deleting its own copy of the token — see
  [`nuxt-huia-headless`](/nuxt-headless/overview)'s `logout()`, which is purely local for this reason.

## Endpoints — `MapHuiaHeadlessEndpoints()`

```
endpoints.MapGroup("identity").MapIdentityApi<HuiaUser>();   // register, login, refresh, confirmEmail,
                                                              // resendConfirmationEmail, forgotPassword,
                                                              // resetPassword, 2FA, /manage/info
endpoints.MapHuiaHeadlessMeEndpoints();                      // GET identity/me
endpoints.MapHuiaHeadlessPasskeyEndpoints();                 // identity/passkey/*, identity/manage/passkeys/*
endpoints.MapHuiaHeadlessPhoneEndpoints();                   // identity/phone/*
endpoints.MapHuiaHeadlessExternalEndpoints();                // identity/account/external/*
```

The framework's own [`MapIdentityApi<TUser>()`](https://learn.microsoft.com/aspnet/core/security/authentication/identity-api-authorization)
does the heavy lifting (this is the same `identity-api-authorization` pattern Microsoft documents for
SPAs); `Huia.Headless` adds four things on top:

| Route | Auth | Purpose |
|---|---|---|
| `GET identity/me` | bearer | Richer claims than `MapIdentityApi`'s stock `/manage/info` (`email`/`isEmailConfirmed` only) — `sub`, `email`, `phoneNumber`, `firstName`, `lastName`, `roles`, so a client doesn't have to reverse-engineer claims out of an intentionally opaque token. |
| `POST identity/passkey/assertion-options` / `assertion` | anonymous | Discoverable passkey sign-in — sets `signInManager.AuthenticationScheme = IdentityConstants.BearerScheme` before signing in, since the passkey ceremony otherwise assumes a cookie. |
| `identity/manage/passkeys` (`GET`/`POST`), `{id}` (`PATCH`/`DELETE`) | bearer | Credential management, reusing `HuiaPasskeyRegistrar<TUser>` — the same registrar `Huia.OpenId` uses. |
| `POST identity/phone/start` / `verify` / `complete-profile` | anonymous | Passwordless SMS one-time-code login — the JSON counterpart to `Huia.OpenId`'s `Login`/`VerifyOtp`/`CompleteProfile` Razor pages. `start` normalizes + rate-limits + sends a code and returns an opaque `flowId`; `verify` checks the code (returning a bearer token directly for an existing account with a complete profile, or `{ flowId, requiresProfile: true }` for a new phone signup or a blank-name existing account); `complete-profile` takes a first/last name and returns the bearer token. |
| `GET identity/account/external/{provider}` / `callback`, `POST .../exchange` / `complete-profile` | mixed | External login — see below. |

Passkey and phone-login routes 404 unless the tenant's `Authentication.UsePasskeyLogin()` /
`Authentication.UsePhoneLogin()` is configured — same opt-in as `Huia.OpenId`. Phone login shares its
implementation (`IPhoneNumberService`, `IOtpService<TUser>`, `IOtpRateLimiter`, `IPhoneLoginRateLimiter`,
`IPendingPhoneSignup`, `ISmsSender`, `ICaptchaVerifier`) with `Huia.OpenId` — all of it lives in core
`Huia.Services`, registered by both `AddHuiaOpenId()` and `AddHuiaHeadless()`. The one Headless-only
piece is `IPhoneLoginFlowStore`, which correlates a `start` call with its later `verify`/`complete-profile`
call — the role an encrypted flow token round-tripped through a hidden form field plays across
`Huia.OpenId`'s page loads, played here by an opaque, server-held id instead (there is no page
navigation to carry state across steps).

## External login

`Huia.OpenId`'s external login is built entirely on the OpenIddict client; `Huia.Headless` has zero
dependency on OpenIddict, so it implements the same `ExternalLoginOptions`/`ExternalProviderRegistration`
configuration (shared with `Huia.OpenId` — `tenant.Authentication.UseExternalLogin(ext => ext.AddGoogle(...))`
works for either flavor) through the classic ASP.NET Core remote-authentication handlers instead:
`AddGoogle`, `AddMicrosoftAccount`, `AddOpenIdConnect`, and a generic `AddOAuth` for GitHub (no official
Microsoft handler exists for it, and its endpoints are stable enough not to need a third-party package).

The redirect shape differs from `Huia.OpenId` in one important way: the browser starts on the *app's*
own origin (not Huia.Headless's), gets sent to the provider, and needs to land back on the app's origin
— never Huia's. So the callback never hands the browser a token directly (that would put it in a URL,
and thus browser history); it hands back a short-lived, single-use **code** instead, via a redirect to
a caller-supplied `returnUrl`:

```
GET  identity/account/external/{provider}?returnUrl=https://shop.example.com/auth/callback
     -> 302 to the real provider (Google/GitHub/etc.)
     -> (provider's own sign-in UI)
     -> 302 back to Huia.Headless's own callback dispatcher
     -> 302 to {returnUrl}?code=<one-time code>          (never a token)

POST identity/account/external/exchange       { code }
     -> a linked/existing account: the bearer token response body directly
     -> a first-time sign-up: { code, requiresProfile: true, email?, firstName?, lastName? }

POST identity/account/external/complete-profile   { code, firstName, lastName }
     -> creates the account, returns the bearer token response body
```

The app's own **server** calls `exchange`/`complete-profile` (mirroring how `nuxt-huia-headless`'s
server proxies `register`/`login`) — the code passes through the browser, the token never does.

Because the challenge's `returnUrl` crosses onto a *different* origin than Huia itself, it can't be
validated the way `Huia.OpenId`'s same-origin `returnUrl` is (a plain same-origin check) — an
unvalidated cross-origin `returnUrl` here would be a textbook open redirect. So it's checked against an
explicit allow-list instead:

```csharp
tenant.Authentication.UseExternalLogin(ext =>
{
    ext.AddGoogle(clientId, clientSecret);
    ext.AllowReturnUrlPrefix("https://shop.example.com/");   // required — AddHuiaHeadless() throws without it
});
```

`AddHuiaHeadless()` throws at start-up if external login is enabled with no `AllowReturnUrlPrefix(...)`
configured — this is enforced, not just documented.

Only a logged-out sign-in/sign-up is implemented — linking a provider to an already-authenticated
account (`Huia.OpenId`'s "Scenario 1", from an account-settings page) is not.

## What's implemented today

Password login (via `MapIdentityApi`), passkeys, phone login, and external login are all wired up.
Password, passkeys, and phone login are demonstrated end to end by the
[Shop sample](https://github.com/Ayman-Elfaki/Huia/tree/main/samples/Shop.Api) — `Shop.App`'s login
page has a phone tab alongside email/password — and covered by its
[e2e tests](https://github.com/Ayman-Elfaki/Huia/tree/main/tests/Huia.E2ETests). External login is
covered by `HeadlessExternalLoginTests` in `Huia.IntegrationTests` but not wired into the Shop
sample's UI: those tests exercise the dispatch/exchange/complete-profile logic by signing directly
into the intermediate `IdentityConstants.ExternalScheme`, standing in for a real provider's callback,
since a live Google/GitHub/etc. account is needed for the actual challenge round trip — the same
reason there's no live e2e coverage of it either.

## First-party Nuxt client

[`nuxt-huia-headless`](/nuxt-headless/overview) is the matching Nuxt 4 module — a plain `fetch`-based
JSON client (no `openid-client`, no discovery document), with the same dual-layer session principle
as `nuxt-huia-oidc`: the browser only ever holds a sealed session cookie, tokens live server-side.

## Sample

`samples/Shop.Api` + `samples/Shop.App` is the reference integration: a single combined app (identity
+ resource endpoints, since Headless tokens are only valid against the app that minted them) wired
through `Huia.AppHost`, with a Nuxt front end using `nuxt-huia-headless` for register/login, a cart,
and checkout.
