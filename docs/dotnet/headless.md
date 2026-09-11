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
```

The framework's own [`MapIdentityApi<TUser>()`](https://learn.microsoft.com/aspnet/core/security/authentication/identity-api-authorization)
does the heavy lifting (this is the same `identity-api-authorization` pattern Microsoft documents for
SPAs); `Huia.Headless` adds three things on top:

| Route | Auth | Purpose |
|---|---|---|
| `GET identity/me` | bearer | Richer claims than `MapIdentityApi`'s stock `/manage/info` (`email`/`isEmailConfirmed` only) — `sub`, `email`, `phoneNumber`, `firstName`, `lastName`, `roles`, so a client doesn't have to reverse-engineer claims out of an intentionally opaque token. |
| `POST identity/passkey/assertion-options` / `assertion` | anonymous | Discoverable passkey sign-in — sets `signInManager.AuthenticationScheme = IdentityConstants.BearerScheme` before signing in, since the passkey ceremony otherwise assumes a cookie. |
| `identity/manage/passkeys` (`GET`/`POST`), `{id}` (`PATCH`/`DELETE`) | bearer | Credential management, reusing `HuiaPasskeyRegistrar<TUser>` — the same registrar `Huia.OpenId` uses. |
| `POST identity/phone/start` / `verify` / `complete-profile` | anonymous | Passwordless SMS one-time-code login — the JSON counterpart to `Huia.OpenId`'s `Login`/`VerifyOtp`/`CompleteProfile` Razor pages. `start` normalizes + rate-limits + sends a code and returns an opaque `flowId`; `verify` checks the code (returning a bearer token directly for an existing account with a complete profile, or `{ flowId, requiresProfile: true }` for a new phone signup or a blank-name existing account); `complete-profile` takes a first/last name and returns the bearer token. |

Passkey and phone-login routes 404 unless the tenant's `Authentication.UsePasskeyLogin()` /
`Authentication.UsePhoneLogin()` is configured — same opt-in as `Huia.OpenId`. Phone login shares its
implementation (`IPhoneNumberService`, `IOtpService<TUser>`, `IOtpRateLimiter`, `IPhoneLoginRateLimiter`,
`IPendingPhoneSignup`, `ISmsSender`, `ICaptchaVerifier`) with `Huia.OpenId` — all of it lives in core
`Huia.Services`, registered by both `AddHuiaOpenId()` and `AddHuiaHeadless()`. The one Headless-only
piece is `IPhoneLoginFlowStore`, which correlates a `start` call with its later `verify`/`complete-profile`
call — the role an encrypted flow token round-tripped through a hidden form field plays across
`Huia.OpenId`'s page loads, played here by an opaque, server-held id instead (there is no page
navigation to carry state across steps).

## What's implemented today

Password login (via `MapIdentityApi`), passkeys, and phone login are wired up. Password and passkeys
are covered by the [Shop sample](https://github.com/Ayman-Elfaki/Huia/tree/main/samples/Shop.Api) and
its [e2e tests](https://github.com/Ayman-Elfaki/Huia/tree/main/tests/Huia.E2ETests); phone login is
covered by `HeadlessPhoneLoginTests` in `Huia.IntegrationTests` (not yet wired into the Shop sample's
UI). External login — a first-class flow in `Huia.OpenId` — has **not** been ported: `Huia.OpenId`'s
implementation is built entirely on the OpenIddict client, which `Huia.Headless` deliberately has zero
dependency on, so it needs a materially different design (most likely a redirect to the provider
followed by a short-lived one-time code exchanged via `POST` for the bearer token, keeping the token
itself out of any URL). If your app needs external login today, use `Huia.OpenId` instead.

## First-party Nuxt client

[`nuxt-huia-headless`](/nuxt-headless/overview) is the matching Nuxt 4 module — a plain `fetch`-based
JSON client (no `openid-client`, no discovery document), with the same dual-layer session principle
as `nuxt-huia-oidc`: the browser only ever holds a sealed session cookie, tokens live server-side.

## Sample

`samples/Shop.Api` + `samples/Shop.App` is the reference integration: a single combined app (identity
+ resource endpoints, since Headless tokens are only valid against the app that minted them) wired
through `Huia.AppHost`, with a Nuxt front end using `nuxt-huia-headless` for register/login, a cart,
and checkout.
