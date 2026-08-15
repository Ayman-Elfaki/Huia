# Architecture

Huia is a library you add to your own ASP.NET Core app, not a standalone service. `services.AddHuia(...)`
registers everything; your app supplies the `DbContext` (or a custom store), the request pipeline, and
whatever else it needs.

## Layers

```
┌─────────────────────────────────────────────────────────────┐
│ Your ASP.NET Core app                                        │
│                                                                │
│  ┌──────────────┐  ┌───────────────────┐  ┌────────────────┐ │
│  │ Identity UI   │  │ Connect endpoints  │  │ Manage/Admin   │ │
│  │ (Razor Pages) │  │ (OpenIddict)       │  │ endpoints      │ │
│  └──────┬───────┘  └─────────┬──────────┘  └───────┬────────┘ │
│         │                    │                      │         │
│  ┌──────┴────────────────────┴──────────────────────┴──────┐  │
│  │           ASP.NET Core Identity + OpenIddict server       │  │
│  └──────┬─────────────────────────────────────┬─────────────┘  │
│         │                                     │                │
│  ┌──────┴──────────┐                 ┌────────┴──────────┐    │
│  │ IHuiaStore /     │                 │ IHuiaSigningKey-  │    │
│  │ Identity stores  │                 │ Store             │    │
│  │ (Huia.EFCore or  │                 │ (Huia.EFCore or   │    │
│  │  your own)       │                 │  your own)        │    │
│  └──────────────────┘                 └───────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

## Request pipeline

`AddHuia(issuer, configure)` registers:

- ASP.NET Core Identity (`AddIdentityCore<HuiaUser>().AddRoles<HuiaRole>().AddSignInManager()`), using the
  standard `Identity.Application` cookie scheme — the same one `SignInManager<HuiaUser>`'s high-level methods
  target, so Huia's own Razor Pages and any custom pages you add work identically. `DefaultSignInScheme` is
  `IdentityConstants.ExternalScheme` (not `ApplicationScheme`) — every `SignInAsync`/`AuthenticateAsync` call
  Huia itself makes passes its scheme explicitly, so this only affects a provider registered via
  `huia.Authentication.UseExternalAuthenticationFlow(ext => ext.Providers...)` (see
  [external-providers.md](external-providers.md)) without its own `SignInScheme`: it lands in the external
  cookie `SignInManager.GetExternalLoginInfoAsync()` reads from, the same default plain ASP.NET Core Identity
  uses, instead of signing straight into the main application cookie unvalidated.
- Sign-in methods are configured via `huia.Authentication` — `UseEmailAndPasswordFlow()` (Huia's original
  method), `UsePasswordlessFlow()` (phone number + SMS OTP, no password — see
  [passwordless.md](passwordless.md)), and `UseExternalAuthenticationFlow(...)`. At least one of the first two
  must be called — `AddHuia` throws otherwise — and any combination may be active at once; the Identity UI's
  `Login` page renders a Basecoat UI tab per active
  1FA method (email+password, phone) when more than one is enabled.
- An OpenIddict authorization server (`AddOpenIddict().AddServer(...)`) with authorization code, refresh
  token, client credentials, and device authorization flows enabled, plus local token validation
  (`AddValidation().UseLocalServer()`) so the same app that issues tokens can also protect its own endpoints
  with `[Authorize]` and `RequireScope(...)`. `huia.Server.RequireAudiences(...)` additionally rejects a
  token whose `aud` claim doesn't include one of the given values — `aud` is only populated for scopes that
  have a resource registered against them (the `/admin/scopes` endpoints' `Resources` field), so this is
  opt-in rather than always-on.
- `RouteOptions.LowercaseUrls = true`, so Razor Pages routes (which default to PascalCase) come out lowercase
  to match the connect/manage/admin endpoints' own hardcoded lowercase paths.
- `AddLocalization()` + `RequestLocalizationOptions` from `huia.Localization` (English + Arabic by default).

`app.UseHuia()` adds:

- `UseRequestLocalization()` — applies the culture requested via query string, cookie, or `Accept-Language`.
- `UseExceptionHandler("/identity/error")` + `UseStatusCodePagesWithReExecute("/identity/error/{0}")` — a
  branded error page for unhandled exceptions and for status-code responses that can't redirect back to the
  client (in particular, OpenIddict's connect endpoints failing with e.g. an unknown `client_id`).

## Persistence

- **`IHuiaStore<TApplication, TAuthorization, TScope, TToken>`** — Identity's user/role/external-login
  stores, OpenIddict's application/authorization/scope/token stores, and signing/encryption key storage
  (`ISigningKeyStore`), as one interface. `Huia.EntityFrameworkCore`'s `WithEntityFrameworkStores<TContext>()`
  implements the Identity/OpenIddict part against EF Core (with its own separate `ISigningKeyStore` for
  keys); implement `IHuiaStore` yourself for a fully custom backend (see [custom-store.md](custom-store.md)).
  Signing-key storage doesn't have to live wherever the rest of your data lives — register a custom
  `ISigningKeyStore` directly to point it at a dedicated backend (a cloud KMS, for instance) independent of
  everything else. Needed only if you enable key management (see [key-management.md](key-management.md)).

## Applications and scopes

`huia.Applications` (an `ApplicationsBuilder`) records client-application declarations
(`AddSinglePageApplication`, `AddServerSideWebApplication`, etc.) during `AddHuia(...)`. A hosted service,
`HuiaApplicationSeeder`, upserts them into the OpenIddict application store on every startup —
`OpenIddictApplicationDescriptorFactory` maps each declared application to the right
permissions/requirements for its kind (public vs. confidential, which grant types, whether PKCE is required).
Every scope any application declares via `AllowScopes(...)` is also registered as a server-recognized scope
automatically.

## External providers

`ext.Providers` (inside `huia.Authentication.UseExternalAuthenticationFlow(ext => {...})`) is the same
`AuthenticationBuilder` `AddHuia` itself uses for the Identity cookie schemes, exposed directly so any
standard ASP.NET Core remote-authentication handler (`AddGoogle`, `AddOpenIdConnect`, `AddOAuth`, ...)
registers against it unmodified. `LoginModel` lists registered schemes via
`SignInManager<HuiaUser>.GetExternalAuthenticationSchemesAsync()`; `ExternalLoginModel`/
`ExternalLoginConfirmationModel` (`Areas/Identity/Pages/Account`) drive the challenge/callback/account-
creation flow, and `ManageExternalLoginsEndpoints` lets a signed-in user list/link/unlink providers on their
own account. See [external-providers.md](external-providers.md).

## Passwordless phone sign-in

`huia.Authentication.UsePasswordlessFlow(...)` enables phone number + SMS one-time-code sign-in — no
password. `PhoneLoginModel`/`PhoneLoginVerifyModel`/`PhoneLoginConfirmationModel`
(`Areas/Identity/Pages/Account`) drive the request-code/verify-code/collect-name-on-first-sign-in flow, backed
by a dedicated `Huia.PhoneVerification` cookie scheme (not TempData) to carry the pending phone number
tamper-proof between the GET and POST steps, `IPhoneOtpRateLimiter` (configurable per-phone-number rate
limiting), `IHuiaPhoneNumberStore` (phone-number lookup, composed into `IHuiaStore`), and
`ISmsSender<HuiaUser>` for delivery. See [passwordless.md](passwordless.md), including its hybrid-auth
security considerations for when this and email+password are both enabled.

## Claims and tokens

`ClaimsHelpers.CreateUserIdentityAsync` builds the `ClaimsIdentity` for a signed-in user: `sub`, `email`,
`name`, `preferred_username`, `given_name`, `family_name`, and `role` claims, with destinations set so
`profile`/`email`/`role` claims only reach the identity token when the corresponding scope was actually
granted (everything always reaches the access token).

## Events

`IHuiaEventPublisher`/`IHuiaEventHandler<TEvent>` is a lightweight pub/sub hook for things happening inside
Huia's own pages — currently `UserRegisteredEvent<TKey>` and `UserSignedInEvent<TKey>` (generic over the
identity user's key type; Huia's own pages always publish `<string>`, since `HuiaUser` uses `IdentityUser`'s
default string key), published from the Register page and equally from the external-login
sign-in/confirmation pages. Register your own `IHuiaEventHandler<T>` implementations to react to them (e.g.
send a welcome email, publish to a message bus) without forking the pages themselves.

## Sample architecture

The [Todo sample](samples.md) demonstrates all of this in one combined host (the API that issues tokens is
also the API that's protected by them) — see [samples.md](samples.md) for how the pieces there fit together.
