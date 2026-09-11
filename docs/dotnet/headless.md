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
```

The framework's own [`MapIdentityApi<TUser>()`](https://learn.microsoft.com/aspnet/core/security/authentication/identity-api-authorization)
does the heavy lifting (this is the same `identity-api-authorization` pattern Microsoft documents for
SPAs); `Huia.Headless` adds two things on top:

| Route | Auth | Purpose |
|---|---|---|
| `GET identity/me` | bearer | Richer claims than `MapIdentityApi`'s stock `/manage/info` (`email`/`isEmailConfirmed` only) — `sub`, `email`, `phoneNumber`, `firstName`, `lastName`, `roles`, so a client doesn't have to reverse-engineer claims out of an intentionally opaque token. |
| `POST identity/passkey/assertion-options` / `assertion` | anonymous | Discoverable passkey sign-in — sets `signInManager.AuthenticationScheme = IdentityConstants.BearerScheme` before signing in, since the passkey ceremony otherwise assumes a cookie. |
| `identity/manage/passkeys` (`GET`/`POST`), `{id}` (`PATCH`/`DELETE`) | bearer | Credential management, reusing `HuiaPasskeyRegistrar<TUser>` — the same registrar `Huia.OpenId` uses. |

Passkey routes 404 unless the tenant's `Authentication.UsePasskeyLogin()` is configured — same
opt-in as `Huia.OpenId`.

## What's implemented today

Password login (via `MapIdentityApi`) and passkeys are wired up and covered by the
[Shop sample](https://github.com/Ayman-Elfaki/Huia/tree/main/samples/Shop.Api) and its
[e2e tests](https://github.com/Ayman-Elfaki/Huia/tree/main/tests/Huia.E2ETests). Phone login and
external login — both first-class flows in `Huia.OpenId` — have **not** been ported to `Huia.Headless`
yet; there is no `identity/passkey`-style endpoint pair for either. If your app needs them today, use
`Huia.OpenId` instead, or treat this as an open contribution area.

## First-party Nuxt client

[`nuxt-huia-headless`](/nuxt-headless/overview) is the matching Nuxt 4 module — a plain `fetch`-based
JSON client (no `openid-client`, no discovery document), with the same dual-layer session principle
as `nuxt-huia-oidc`: the browser only ever holds a sealed session cookie, tokens live server-side.

## Sample

`samples/Shop.Api` + `samples/Shop.App` is the reference integration: a single combined app (identity
+ resource endpoints, since Headless tokens are only valid against the app that minted them) wired
through `Huia.AppHost`, with a Nuxt front end using `nuxt-huia-headless` for register/login, a cart,
and checkout.
