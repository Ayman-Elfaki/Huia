# Huia — Technical Specification

> A multi-tenant **OpenID Connect / OAuth 2.0 Identity Provider** for ASP.NET Core, shipped as three
> NuGet packages. Tenants are isolated over base-path routing (`/{tenant}/…`) with per-tenant signing
> keys, discovery documents and issuers, per-tenant Identity policy, and a batteries-included Razor
> account UI.

- **Target framework:** `net10.0` (C# 14, `LangVersion=latest`)
- **Packages:** `Huia`, `Huia.EntityFrameworkCore`, `Huia.AspNetCore`
- **Core dependencies:** OpenIddict 7.x (server **and** client), Finbuckle.MultiTenant 10.x, EF Core /
  ASP.NET Core Identity 10.x, Quartz 3.x, `Microsoft.Extensions.Caching.Hybrid`
- **Configuration section:** `Huia`
- **Companion:** the Nuxt 4 relying-party module — see [`../nuxt/SPEC.md`](../nuxt/SPEC.md)

---

## Table of contents

1. [Overview & Architecture](#1-overview--architecture)
2. [Packages](#2-packages)
3. [Options tree & validation](#3-options-tree--validation)
4. [Multi-tenancy](#4-multi-tenancy)
5. [Identity & `HuiaUserManager`](#5-identity--huiausermanager)
6. [OpenIddict server & client](#6-openiddict-server--client)
7. [Key lifecycle](#7-key-lifecycle)
8. [Endpoints](#8-endpoints)
9. [Security Considerations](#9-security-considerations)
10. [Testing Strategy](#10-testing-strategy)

---

## 1. Overview & Architecture

### 1.1 Summary

Huia turns an ASP.NET Core host into an OIDC provider that serves **many isolated tenants** from one
process and one database. A tenant is a URL base-path segment (`acme` → everything lives under
`/acme/…`), an EF Core row-scope, a signing-key set, an issuer (`{Issuer}/acme`), and a bundle of
per-tenant Identity + sign-in policy.

Three wiring calls:

```csharp
builder.Services.AddDbContext<HuiaDbContext>(o => o.UseNpgsql(cs).UseOpenIddict());

builder.Services.AddHuia(huia =>
    {
        huia.UseIssuer("https://id.example.com");
        huia.AddTenant("acme", tenant =>
        {
            tenant.Authentication.UsePasswordFlow(p => p.MinimumLength = 12);
            tenant.AddServerSideWebApplication("acme-web", "secret", client =>
            {
                client.RedirectUris.Add(new Uri("https://acme.example.com/callback"));
                client.RequirePushedAuthorizationRequests();
            });
        });
    })
    .AddHuiaUi()
    .AddHuiaSecurityHeaders();

var app = builder.Build();
app.UseHuia();                     // pipeline order below
app.MapHuiaEndpoints();            // connect + manage + account UI + external login
app.MapHuiaAdminEndpoints()
   .RequireAuthorization(p => p.RequireTenants("master").RequireRole(HuiaConstants.Roles.Administrator));
```

### 1.2 `UseHuia()` pipeline order

`UseHuia()` fixes the middleware order (`HuiaApplicationBuilderExtensions.cs`):

```
UseExceptionHandler("/identity/account/status/500")
  → UseStatusCodePagesWithReExecute("/identity/account/status/{0}")   styled error pages; bare codes
      for /connect, /manage, /admin, /.well-known + JSON
  → UseRequestLocalization()                                          en / ar (RTL)
  → UseMultiTenant()               ← MUST precede UseRouting: the base-path strategy rebases
      PathBase to /{tenant} so routed endpoints match /connect/token, not /{tenant}/connect/token
  → HuiaSecurityHeadersMiddleware  ← only when AddHuiaSecurityHeaders() registered its marker;
      sits after UseMultiTenant so the CSP can use tenant branding + a per-request nonce
  → UseStaticFiles()
  → UseRouting()
  → UseAuthentication()            ← OpenIddict validation + the Identity cookies + external scheme
  → UseAuthorization()
```

### 1.3 Request & token flow

```
  Browser ──GET /acme/connect/authorize?…──▶ OpenIddict server (passthrough)
                                              │  not signed in → 302 to the Razor account UI
                                              │  /acme/identity/account/login
  Browser ──password / SMS OTP / external──▶ account UI ──SignInManager cookie (huia.auth.acme)──┐
                                                                                                 │
  Browser ◀── 302 back to /acme/connect/authorize ──────────────────────────────────────────────┘
          ── code ▶ RP
  RP ──POST /acme/connect/token (form-urlencoded, PKCE)──▶ OpenIddict server
                                                            │ AuthorizeAsync builds the principal
                                                            │ (sub, role[], tenant, scopes)
                                                            │ HuiaTenantSigningKeyHandler swaps in
                                                            │   the tenant's rotated RSA key
                                                            │ issuer = {Issuer}/acme
  RP ◀── access_token (JWT) + id_token + refresh_token ─────┘
  Resource API ──GET {Issuer}/acme/.well-known/openid-configuration──▶ per-tenant discovery
               ──GET {Issuer}/acme/.well-known/jwks──▶ HuiaTenantJwksHandler serves the tenant's
                                                        published keys
```

`HuiaDbContext` derives from Finbuckle's `MultiTenantIdentityDbContext<HuiaUser, HuiaRole, string>`:
a **global query filter** (`TenantId == TenantInfo.Id`) scopes every read of a user/role/claim/login/
token entity, and `SaveChanges` runs `EnforceMultiTenant()` (throws on a tenant-less or cross-tenant
write, auto-stamps `TenantId` on insert).

---

## 2. Packages

| Package | Contents | Depends on |
|---|---|---|
| **`Huia`** | Domain model (`HuiaSigningKey` and value objects), the **options tree** (`HuiaOptions` → `TenantOptions` → …), eventing abstractions (`IHuiaEventPublisher`, `HuiaEvents`), constants (`HuiaConstants`). | Nothing framework-specific. A build `Target` fails the compile if an ASP.NET Core / EF Core `PackageReference` is added. |
| **`Huia.EntityFrameworkCore`** | `HuiaDbContext : MultiTenantIdentityDbContext<HuiaUser, HuiaRole, string>`; `ModelBuilder.UseOpenIddict()`; every table renamed `Huia*` via `ToTable`; the named tenant-scoped composite indexes (`IX_HuiaUsers_Tenant_UserName` unique, `…_Tenant_Email`, `IX_HuiaRoles_Tenant_Name` unique) applied last in `OnModelCreating` (`ApplyTenantScopedIndexes`, superseding Finbuckle's `AdjustUniqueIndexes`); `IMultiTenantContextAccessor` extensions (`CurrentTenantId()` / `RequireCurrentTenantId()`); `HuiaTenantScope.Enter(...)` for seeding / admin. **Ships no migrations** — the consuming app owns the provider and the migration assembly. |
| **`Huia.AspNetCore`** | `AddHuia()` / `UseHuia()` / `MapHuiaEndpoints()` / `MapHuiaAdminEndpoints()`; OpenIddict **server** + **client** config; the Razor Pages account UI (`Areas/Identity/Pages/Account/**`, en/ar, RTL, a committed JS bundle under `wwwroot/`); passwordless SMS (`IOtpService`, `IPhoneNumberService`, `IPendingPhoneSignup`, rate limiters); `HuiaUserManager`; the key-lifecycle Quartz jobs + `IHuiaKeyRing`; `AddHuiaSecurityHeaders()`; `IHuiaEmailSender` (MailKit) + Razor email rendering. |

`Huia` and `Huia.EntityFrameworkCore` expose `internal` members to `Huia.AspNetCore` and the four test
projects via `InternalsVisibleTo` in `src/Directory.Build.props`.

---

## 3. Options tree & validation

### 3.1 Shape

```
HuiaOptions                                     (root; bound from "Huia" or built via HuiaOptionsBuilder)
├─ Issuer : Uri                                 required; per-tenant issuer = {Issuer}/{tenant}
├─ PublicUrl : Uri?                             absolute-link base for out-of-request emails
├─ DisableTransportSecurityRequirement : bool   dev / in-process tests only
├─ Email : EmailOptions                         root SMTP; per-tenant override merges over it
├─ Sms : SmsOptions                             root SMS;  per-tenant override merges over it
├─ Keys : KeyManagementOptions                  signing-key lifecycle (shared across tenants)
└─ Tenants : IDictionary<string, TenantOptions> keyed by tenant id (also the base-path segment)
   └─ TenantOptions
      ├─ DisplayName : string?
      ├─ Authentication : HuiaTenantAuthenticationOptions       ── fluent-only, see §3.2
      ├─ Lockout : TenantLockoutOptions                         MaxFailedAccessAttempts / LockoutDuration / AllowedForNewUsers
      ├─ Branding : TenantBrandingOptions                       DisplayName / Logo / Favicon / Accent / Terms / Privacy / Support
      ├─ Email : EmailOptions?                                  merged over HuiaOptions.Email
      ├─ Sms : SmsOptions?                                      merged over HuiaOptions.Sms
      ├─ Clients : IList<HuiaClientDescriptor>                  tenant.AddClient(id, kind) / AddServerSideWebApplication / …
      └─ Scopes : IList<HuiaScopeDescriptor>                    tenant.AddScope(name, …)  (code-defined scopes are read-only in the admin UI)
```

### 3.2 `HuiaTenantAuthenticationOptions` — fluent-only

The password and passwordless option objects are **not public**. Configure exclusively through the
`Use*` methods; read enablement through the derived bools:

```csharp
tenant.Authentication
    .UsePasswordFlow(p =>                       // sets IsPasswordEnabled; p is PasswordFlowOptions
    {
        p.MinimumLength = 12;
        p.RequireNonAlphanumeric = true;
        p.RequireConfirmedEmail = false;
        p.RequireUniqueEmail = true;
        p.AllowSelfServiceRegistration = false; // or tenant.DisableRegistration()
    })
    .UsePasswordlessFlow(pwl =>
    {
        pwl.UsePhoneLogin(phone =>             // sets IsPhoneLoginEnabled
        {
            phone.DefaultCountry = "SA";       // ISO 3166-1 alpha-2 — moved here from the umbrella
            phone.AllowAutoProvisioning = true;
            phone.CodeLength = 6;
            phone.CodeLifetime = TimeSpan.FromMinutes(5);
            phone.SuccessfulLoginsPerWindow = 1;
            phone.SuccessfulLoginWindow = TimeSpan.FromMinutes(2);
            phone.SuccessfulLoginsPerDay = 5;
        });
        pwl.UseExternalLogin(ext =>            // sets IsExternalLoginEnabled once a provider is added
        {
            ext.AddGoogle("id", "secret");
            ext.AddOpenIdConnect("Partner", "id", "secret", "https://partner.example/");
            ext.EnableAccountsLinking();       // was LinkExistingAccountsByEmail(); sets AccountLinkingEnabled
        });
    });

// reads:
tenant.Authentication.IsPasswordEnabled       // => PasswordFlowOptions.Enabled
tenant.Authentication.IsPhoneLoginEnabled     // => a PhoneLoginOptions was created
tenant.Authentication.IsExternalLoginEnabled  // => at least one provider registered
```

Framework code inside `Huia` / `Huia.AspNetCore` reads the full objects through the `internal`
`Authentication.Password` / `Authentication.Passwordless` accessors.

### 3.3 `HuiaClientDescriptor`

`Kind` (`ServerSideWebApplication` | `SinglePageApplication` | `NativeApplication` | `MachineToMachine`)
drives default grants + endpoint permissions. `RequirePkce`, `RequireConsent` are `bool` properties;
`RequirePushedAuthorizationRequests()` is a **fluent method** (backing property
`RequiresPushedAuthorizationRequests`) — it adds the OpenIddict `ft:par` requirement in
`HuiaApplicationDescriptorMapper`; `ept:pushed_authorization` is granted to every interactive client
regardless. `Token` (`TokenLifetimeOptions`) sets per-client `AccessToken` / `IdentityToken` /
`RefreshToken` / `AuthorizationCode` / `DeviceCode` / `UserCode` lifetimes.

### 3.4 Validation

Dependency-free, one pass, reports **everything**:

```csharp
internal interface IHuiaOptionsSection
{
    void Validate(string path, List<string> errors);   // append "<path>: <message>", never throw
}
// helpers: HuiaOptionsValidation.Combine(path, member)  → "a:b"
//          errors.Require(condition, path, message)
```

`HuiaOptions.Validate()` (also called from `HuiaOptionsBuilder.Build()`) walks the whole tree —
`Email` / `Sms` / `Keys`, each `Tenants:{id}` → `Authentication` (→ `Password` + `Passwordless` →
`PhoneLogin?` / `ExternalLogin` → `Providers[i]`) / `Lockout` / `Branding` / `Clients[i]` /
`Scopes[i]` — appending `"Huia:Tenants:acme:Authentication:Passwordless:PhoneLogin:DefaultCountry:
must be a two-letter …"`-style messages, then throws `HuiaOptionsException(IReadOnlyList<string>
Errors)` if any. Cross-field rules include "at least one sign-in method enabled per tenant",
`PendingSignupLifetime >= CodeLifetime`, `SuccessfulLoginsPerDay >= SuccessfulLoginsPerWindow`.

### 3.5 `Email` / `Sms` merge gotchas

Only `EmailOptions` and `SmsOptions` have `MergedWith(tenant?)`:

- **Email** — scalar fields fall back tenant → root; but `Port` / `UseSsl` travel with `Host` as a
  unit: a tenant that sets `Host` and not `Port` gets `EmailOptions`' own defaults (587 / true), not
  the root's.
- **Sms** — `LogCodesToLogger` is **OR-merged** (a tenant cannot turn it off); `RateLimit` is **not**
  field-merged — the tenant's `RateLimit` is kept only when the tenant also set its own `Provider`,
  otherwise the root's is used wholesale.

---

## 4. Multi-tenancy

- **Strategy** — Finbuckle base-path (`WithBasePathStrategy`) + `RebaseAspNetCorePathBase`.
  `UseMultiTenant()` runs before `UseRouting()` so `/acme/connect/token` matches the `/connect/token`
  route after the rebase. Finbuckle namespaces: `AddMultiTenant` → `Finbuckle.MultiTenant.Extensions`;
  `UseMultiTenant` / `WithBasePathStrategy` → `Finbuckle.MultiTenant.AspNetCore.Extensions`.
- **Row scoping** — `HuiaDbContext : MultiTenantIdentityDbContext<HuiaUser, HuiaRole, string>`; ctor
  `(IMultiTenantContextAccessor accessor, DbContextOptions<HuiaDbContext> options)` — `TenantInfo` is
  **snapshotted at construction**, so seeding / background code must set the ambient tenant *before*
  resolving `HuiaDbContext` / `UserManager` in the scope (`HuiaTenantScope.Enter(scopedServices,
  tenantId)`). The global filter **NREs** (not "returns nothing") when `TenantInfo` is null — any
  cross-tenant query (admin `ListUsers`, some test helpers) must `.IgnoreQueryFilters()`.
- **`HuiaSigningKey` is NOT `IsMultiTenant()`** — a plain entity with a manual `TenantId`, so the key
  jobs run with no tenant scope. OpenIddict entities are stock (`ToTable("Huia…")` only); the tenant
  binding lives in `Properties["huia:tenant"]` and is enforced at the endpoint / seed layer by
  `HuiaOpenIddict{Application,Scope}Store`.
- **Per-tenant `IdentityOptions`** — `HuiaMultiTenancyConfiguration` projects the resolved tenant's
  `Authentication.Password.*` / `Lockout.*` onto a scoped `IOptions<IdentityOptions>` (Finbuckle
  `ConfigurePerTenant` + an `IOptions` bridge) so `UserManager` / `SignInManager` observe them. The
  process-global `AddIdentity` registration (`HuiaIdentityConfiguration`) keeps the **least
  restrictive** value across all tenants as the fallback.
- **Per-tenant auth** — `WithPerTenantAuthentication()` wraps every cookie scheme's
  `OnValidatePrincipal` to reject a ticket whose `__tenant__` property ≠ the request's resolved
  tenant. Per-tenant cookie names via `ConfigurePerTenant<CookieAuthenticationOptions, HuiaTenantInfo>`
  → `huia.auth.{tenant}` / `huia.2fa.{tenant}` (a browser can hold several tenants' sessions).
  Wiring order in `AddHuia`: `AddHuiaMultiTenancy` → `AddHuiaIdentity` → `AddHuiaCookieHardening` →
  `AddHuiaPerTenantAuthentication`. Cookie hardening is always-on (`huia.auth` `Secure` gated on
  `!DisableTransportSecurityRequirement`, `SameSite=Lax` deliberately so the RP→IdP top-level nav
  keeps the session); `huia.flow` (return-URL protector) `SameSite=Strict`; `huia.csrf` antiforgery
  `SameSite=Strict`; external `CorrelationCookie` `SameSite=None`.

---

## 5. Identity & `HuiaUserManager`

### 5.1 Entities

`HuiaUser : IdentityUser<string>` and `HuiaRole : IdentityRole<string>` (in
`Huia.EntityFrameworkCore.Entities`) add `TenantId`, `FirstName`, `LastName`, and
`HasCompleteProfile`. `HuiaUserConfirmation : IUserConfirmation<HuiaUser>` replaces the stock one:
`SignInManager.CanSignInAsync` returns true when the tenant's password flow does not
`RequireConfirmedEmail`, so a phone-login account (no email) is never blocked on email confirmation.

### 5.2 `HuiaUserType`

`enum { Unknown, Password, External, Phone }` — how an account authenticates, resolved with
precedence **password → external → phone** by `HuiaUserManager.GetUserTypeAsync`. Drives the
`/manage/*` contact-detail rules: a `Password` or `External` account owns an email and must not carry
a phone number; a `Phone` account owns its number (which doubles as the username) and must not carry
an email.

### 5.3 `HuiaUserManager : UserManager<HuiaUser>`

Registered via `.AddUserManager<HuiaUserManager>()` on the `AddIdentity<HuiaUser, HuiaRole>()` chain,
so `SignInManager` and every `UserManager<HuiaUser>` resolution get it. It consolidates the phone /
external operations that were otherwise open-coded across the account UI and the endpoints:

| Member | Replaces |
|---|---|
| `Task<HuiaUserType> GetUserTypeAsync(HuiaUser)` | the `HuiaUserTypeExtensions.ResolveTypeAsync` extension (deleted) |
| `Task<bool> CanRemoveExternalLoginAsync(HuiaUser)` | the `ExternalLoginsModel.CanRemoveLoginAsync` static (deleted) — true unless removing the login would leave no way to sign in (no password, no phone, ≤ 1 login) |
| `Task<HuiaUser?> FindByPhoneNumberAsync(string e164)` | `Users.FirstOrDefault(u => u.PhoneNumber == e164)` (looks up the **column**, not the username) — tenant-scoped by the query filter |
| `Task<HuiaUserCreation> CreatePhoneUserAsync(tenantId, e164, first, last)` | the `new HuiaUser { UserName = e164, PhoneNumber = e164, PhoneNumberConfirmed = true, EmailConfirmed = true } + CreateAsync` in `CompleteProfile` / admin CRUD |
| `Task<IdentityResult> AddExternalLoginAsync(user, provider, key, displayName)` | `AddLoginAsync(user, new UserLoginInfo(…))` |
| `Task<HuiaUserCreation> CreateExternalUserAsync(tenantId, email?, first, last, provider, key, displayName)` | derive-username (email, or a `slug(displayName)-{key}`) + `CreateAsync` + `AddExternalLoginAsync` in `CompleteProfile.CompleteExternalSignupAsync` |
| `Task<(ExternalEmailLinkOutcome Outcome, HuiaUser? User)> TryLinkExternalByEmailAsync(email, providerVouches, accountLinkingEnabled, provider, key, displayName)` | the `FindByEmailAsync` + `AccountLinkingEnabled && EmailConfirmed && providerVouches` + `AddLoginAsync` block in `ExternalEndpoints.ExternalLoginCallbackAsync` — `NoMatch` (provision), `Linked` (sign in), `Blocked` (refuse, never duplicate) |

`HuiaUserCreation` is `readonly record struct (IdentityResult Result, HuiaUser User)` with
`Succeeded => Result.Succeeded`.

### 5.4 Passwordless SMS

- `IOtpService` / `OtpService` — the code is `RandomNumberGenerator.GetInt32`, stored as
  `SHA-256(salt ‖ code)` in `AspNetUserTokens` (login provider `Huia.Passwordless`, name `otp`, JSON
  `{Hash, Salt, ExpiresUtc, Attempts}`), single-use, constant-time compare (`FixedTimeEquals`),
  attempt cap, `TimeProvider` for expiry. **No EF migration** — `AspNetUserTokens` already exists.
- `IPendingPhoneSignup` — an in-memory store of `{tenant, e164, hashedCode, expiresUtc}` used so an
  auto-provisioning signup **never persists a blank-name user**: `Login` → `VerifyOtp` verify against
  the pending record → `CompleteProfile` calls `HuiaUserManager.CreatePhoneUserAsync`.
- `IPhoneNumberService` (libphonenumber-csharp) — `TryNormalize(input, defaultRegion, out e164)` gated
  on `IsPossibleNumber` (not `IsValidNumber` — the latter rejects the `+1500…` test numbers), and
  `Mask(e164)` → `••••1234`.
- Rate limiting — `IOtpRateLimiter` (per-number issue throttle) + `IPhoneLoginRateLimiter`
  (successful-sign-in ceiling; non-consuming `CanRecordLogin` pre-check before an SMS is spent,
  consuming `TryRecordLogin` on a completed sign-in).

### 5.5 External login

Implemented exclusively through the **OpenIddict client** (never the classic ASP.NET Core
authentication handlers). `RegisterExternalProviders` adds one `OpenIddictClientRegistration` per
`(tenant, provider)` with `RegistrationId = "{tenant}:{name}"` and `RedirectUri = signin-{name}`.
`ExternalEndpoints` maps `POST identity/account/external/{provider}` (challenge), `signin-{provider}`
(callback), `identity/account/externallogincallback` (dispatch — link to the live session, sign in an
already-linked account, `TryLinkExternalByEmailAsync`, or hand off to `CompleteProfile`), and
`signout-callback-oidc` for RP-initiated end-session at the upstream provider.

---

## 6. OpenIddict server & client

### 6.1 Flows

Authorization Code + PKCE (with optional PAR), refresh, client credentials, device authorization, and
passwordless SMS (a cookie sign-in with `amr=sms` that `/connect/authorize` accepts with zero
OpenIddict changes). The token endpoint has its own minimal-API handler in `ConnectEndpoints`
(passthrough enabled); `userinfo` and `logout` are likewise handled there.

### 6.2 Per-tenant signing & validation handlers

Tokens are minted with the **tenant's rotated key** and a tenant issuer, so three custom handlers are
inserted at specific orders:

| Handler | Event | Order | Job |
|---|---|---|---|
| `HuiaTenantSigningKeyHandler` | `GenerateTokenContext` | `int.MinValue + 100_500` | overrides `SigningCredentials` for `urn:…:access_token` / `…:id_token` **only** (overriding the auth code / refresh token breaks `code` exchange with `invalid_grant`) |
| `HuiaTenantTokenValidationHandler` | `ValidateTokenContext` (validation) | `int.MinValue + 101_000` | clones `TokenValidationParameters`, appends the tenant's published `IHuiaKeyRing` keys to `IssuerSigningKeys` and `{Issuer}/{tenant}` to `ValidIssuers` |
| `HuiaTenantJwksHandler` | JWKS request | `int.MaxValue - 100_000` | serves the tenant's published keys at `{Issuer}/{tenant}/.well-known/jwks` |

`GetDestinations` sends `role` + `tenant` claims to both tokens; a client needs
`Permissions.Scopes.Roles` to request `roles` (else `400 invalid_scope`). OpenIddict 7 permission
strings: `ept:end_session` (not `logout`), `ept:device_authorization`, `ft:pkce`, `ft:par`.

### 6.3 PAR

`SetPushedAuthorizationEndpointUris("connect/par")` — no global `RequirePushedAuthorizationRequests`.
`HuiaClientDescriptor.RequirePushedAuthorizationRequests()` → the mapper adds `ft:par`
(`Requirements.Features.PushedAuthorizationRequests`); `ept:pushed_authorization` is granted to every
interactive client. OpenIddict 7.6+ handles the `/connect/par` endpoint internally — no ASP.NET
passthrough. Advertised per tenant as `pushed_authorization_request_endpoint`.

### 6.4 Admin CRUD → OpenIddict application

`HuiaApplicationDescriptorMapper.ToDescriptor(tenantId, HuiaClientDescriptor, origin)` is shared by
`HuiaClientSeeder` (options-tree clients, `origin = static`) and the admin API (`origin = dynamic`).
It writes `Properties["huia:tenant"]`, `["huia:home_uris"]` (JSON), and maps `Token.*` to
`Settings[OpenIddictConstants.Settings.TokenLifetimes.*]` via `TimeSpan.ToString("c", Invariant)`.

---

## 7. Key lifecycle

- **`HuiaSigningKey`** (table `HuiaSigningKeys`, status enum stored as string): per-tenant RSA key,
  private material wrapped with Data Protection. States: `Pending → Active → Rotated → Retired`.
- **`IHuiaKeyRing`** — a `HybridCache`-backed service (not an entity). RSA import is memoised by `kid`
  in a static dictionary. Exposes the active signing key and the published (validation) key set per
  tenant.
- **Quartz jobs** (4) — promote pending → active, rotate the active key on schedule, retire rotated
  keys past the overlap window, delete retired keys. Gated by
  `KeyManagementOptions.EnableBackgroundJobs` (default true; tests set false, as does the
  `Huia:EnableBackgroundJobs` sample flag — Quartz keeps a process-static log provider that throws on
  a second host in the same process).

---

## 8. Endpoints

| Group | Route (under `/{tenant}`) | Auth | Notes |
|---|---|---|---|
| **connect** | `/connect/{authorize,token,userinfo,par,end_session,device,verification}` | OpenIddict | `.well-known/openid-configuration` + `.well-known/jwks` per tenant |
| **account UI** | `/identity/account/{login,register,verifyotp,completeprofile,forgotpassword,resetpassword,confirmemail,externallogins,status}` | cookie | Razor Pages, `Areas/Identity/Pages/Account`, en/ar RTL, one committed JS bundle |
| **external** | `identity/account/external/{provider}`, `signin-{provider}`, `identity/account/externallogincallback`, `signout-callback-oidc` | mixed | OpenIddict-client challenge + dispatch |
| **manage** (`/manage`) | `GET/PUT profile`, `PUT password`, `GET/PUT email` + `POST email/confirm`, `GET/PUT phone` + `POST phone/confirm` + `DELETE phone`, `GET/DELETE external-logins` | bearer (`HuiaConstants.Policies.Api`, pins `OpenIddictValidationAspNetCoreDefaults` scheme) | caller resolved from `sub`; `HuiaUserManager.GetUserTypeAsync` guards which contact detail each account type may change |
| **admin** (`/master/admin`) | `GET tenants`; users / clients / scopes / keys `GET/POST/PUT/DELETE` | `RequireTenants("master").RequireRole(Administrator)` | keyset pagination (`MR.AspNetCore.Pagination`, `KeysetQueryModel` built from the query string); `409` on static clients/scopes; `HuiaClientDescriptor.Validate()` before the mapper |
| **home** | `/` | — | `MapHuiaHome("master")` bounces bare `/` to the master tenant |
| **status** | `/identity/account/status/{code}` | — | `UseStatusCodePagesWithReExecute` target; styled pages for HTML, bare code for `/connect` `/manage` `/admin` `/.well-known` + JSON |

---

## 9. Security Considerations

| Concern | Mitigation |
|---|---|
| Cross-tenant data access | `HuiaDbContext` global query filter (`TenantId == TenantInfo.Id`) on read + `EnforceMultiTenant()` on write; named unique composite indexes are `{TenantId, Normalized*}`. |
| Cross-tenant token replay | `HuiaTenantTokenValidationHandler` pins the tenant's keys + issuer; a token minted for tenant A is `401 invalid_token` at tenant B (fails validation before authz). |
| Session bleed across tenants | `WithPerTenantAuthentication()` rejects a cookie whose `__tenant__` ≠ the request tenant; per-tenant cookie names. |
| Authorization-code interception | PKCE (`ft:pkce`, forced on for public interactive clients). |
| Auth-request tampering / referrer & log leakage | RFC 9126 PAR at `/{tenant}/connect/par`; per-client `RequirePushedAuthorizationRequests()`. |
| OTP brute force / reuse | `SHA-256(salt ‖ code)`, single-use, `FixedTimeEquals`, attempt cap, per-number issue + successful-sign-in rate limits; the ceiling is checked before an SMS is spent. |
| Account enumeration on phone login | unknown number with auto-provisioning off behaves identically to a known number (same redirect, same timing). |
| External sign-in account takeover | link-by-email is off by default; even on, it requires `EmailConfirmed` **and** the provider vouches (`email_verified != false`); otherwise the sign-in is refused and no duplicate is created (`ExternalEmailLinkOutcome.Blocked`). |
| Removing the last sign-in method | `HuiaUserManager.CanRemoveExternalLoginAsync` — an unlink is refused when it would leave no password, no phone and ≤ 1 login. |
| Lockout bypass on token grants | the password grant uses `signInManager.CheckPasswordSignInAsync(lockoutOnFailure: true)`, honouring the per-tenant lockout policy. |
| Open redirect (`returnUrl`, `post_logout_redirect_uri`) | `IReturnUrlProtector` — DP-protected tokens, same-origin sanitisation. |
| Response headers / CSP | `AddHuiaSecurityHeaders()` (opt-in) — per-request-nonce CSP (`script-src 'self' 'nonce-…'`, `style-src` nonce, `form-action` incl. external IdP origins), `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, HSTS when `IsHttps && !DisableTransportSecurityRequirement`. |
| Private key exposure | signing-key private material wrapped with Data Protection; `IHuiaKeyRing` memoises the imported RSA by `kid` only. |
| HTTP in production | OpenIddict transport guard + `Secure` cookies unless `DisableTransportSecurityRequirement` (dev / in-process tests). |
| Transitive-package advisories | `TreatWarningsAsErrors` repo-wide; pinned fixes (`MailKit`/`MimeKit` 4.17, `AngleSharp` 1.7.2, `SSH.NET` ≥ 2026 for the Testcontainers path). |

---

## 10. Testing Strategy

xUnit v2 + **Shouldly** (no `Assert.*`); snake_case sentence method names
(`A_full_authorization_code_pkce_sign_in_yields_tenant_scoped_tokens`); `[Trait("Category", …)]` =
`Container` / `E2E` / `PenTest`; CI default filter
`Category!=Container&Category!=E2E&Category!=PenTest`.

| Project | Scope | Notes |
|---|---|---|
| **`Huia.Tests`** (`src/dotnet/tests/`) | Pure unit — options-tree validation, `Email`/`Sms` merge semantics, `HuiaDbContext` model (table renames, indexes) + tenant query filter. | in-memory SQLite; refs `Huia` + `Huia.EntityFrameworkCore` only. |
| **`Huia.IntegrationTests`** (`src/dotnet/tests/`) | In-process host (`HuiaTestHost`: `HostBuilder` + `UseTestServer` + shared open `SqliteConnection`, schema via `EnsureCreatedAsync` in a hosted service before `AddHuia`). Auth-code + PKCE through the real Razor UI (`AuthorizationCodeFlowTests`, cookie-forwarding handler), refresh, client credentials, device, passwordless SMS (`PhoneFlow`), external login, `/manage` contact rules, admin CRUD, per-tenant `IdentityOptions`, key lifecycle, `HuiaUserManagerTests` (§5.3). Testcontainers-PostgreSQL tests carry `[Trait("Category","Container")]`. | `HuiaTestHost` helpers: `SeedUserAsync`, `SeedPhoneUserAsync`, `SeedInteractiveClientAsync`, `WithUserManagerAsync` (hands a tenant-scoped `HuiaUserManager`), `AddExternalLoginAsync`. |
| **`Huia.Tests.PenTest`** (`src/dotnet/tests/`) | `[Trait("Category","PenTest")]` — cross-tenant token rejection, open-redirect, CSRF / state, privilege escalation, brute-force / rate-limit. Drives `samples/Huia.IdentityServer` out-of-process (port 5310) so the sample's rate limiter is really in the pipeline; a fresh cookie jar per sign-in. | |
| **`tests/Huia.E2ETests`** (repo root) | `[Trait("Category","E2E")]`, `[SkippableFact]` + `Skip.IfNot(fixture.Started, …)`. `InteractiveSignInTests` / `MailFlowE2ETests` drive `samples/Huia.IdentityServer` via Playwright; `AdminUiE2ETests` drives the Aspire AppHost stack; `FrontEndAuthE2ETests` drives the Nuxt Todo.App through real OIDC (password / phone / external); `HuiaAuthNuxtE2ETests` drives the `huia-auth-nuxt` playground (see `../nuxt/SPEC.md` §10.5). Skip-tolerant — a missing build output or start-up failure skips, never fails. | `RepoRoot.Find()` (sentinel `src/dotnet/Huia.slnx`). |

**CI jobs** (`.github/workflows/ci.yml`, `.NET` jobs run in `src/dotnet`): `build` (build + the
default-filter tests), `pen-test`, `integration` (`Category=Container`), `e2e` (builds both Nuxt
samples + the `src/nuxt` playground, installs Playwright, runs `Category=E2E`), `web-assets`
(rebuilds `Huia.AspNetCore/wwwroot` and `git diff --exit-code`), `docs`, `nuxt-auth-module`.
