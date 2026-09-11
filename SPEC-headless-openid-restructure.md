# Huia — Headless / OpenId Package Split — Technical Specification

> Design spec for restructuring Huia from 3 .NET packages + 1 Nuxt module into **6 .NET packages +
> 2 Nuxt modules**, adding a bearer-token "headless" identity flavor (ASP.NET Core Identity API
> style, multi-tenant, with phone login) alongside the existing OpenID Connect provider, and a new
> `Shop` sample exercising it end-to-end.

- **Status:** design spec, nothing in this document is implemented yet.
- **Companions:** [`src/dotnet/SPEC.md`](src/dotnet/SPEC.md) (current `Huia.AspNetCore` OIDC
  design — most of §3–§9 there is the source of truth this spec migrates from) and
  [`src/nuxt/SPEC.md`](src/nuxt/SPEC.md) (current `nuxt-huia` OIDC module design).
- **Verified against:** repo state at commit `ddb806d` (branch `main`, 2026-09-11). File inventory
  in §3 was built by listing every `.cs` file in `src/dotnet/src/{Huia,Huia.AspNetCore,
  Huia.EntityFrameworkCore}` and reading the ones whose classification was not obvious from the
  name; re-verify against current code before implementing, since this is a point-in-time read.

## Table of contents

1. [Overview & goals](#1-overview--goals)
2. [Final package/module matrix & dependency graph](#2-final-packagemodule-matrix--dependency-graph)
3. [Type-by-type migration & rename inventory](#3-type-by-type-migration--rename-inventory)
4. [`Huia` (common) surface spec](#4-huia-common-surface-spec)
5. [`Huia.EntityFrameworkCore` (common) surface spec](#5-huiaentityframeworkcore-common-surface-spec)
6. [`Huia.Headless` surface spec](#6-huiaheadless-surface-spec)
7. [`Huia.Headless.EntityFrameworkCore` surface spec](#7-huiaheadlessentityframeworkcore-surface-spec)
8. [`Huia.OpenId` / `Huia.OpenId.EntityFrameworkCore` — delta from today](#8-huiaopenid--huiaopenidentityframeworkcore--delta-from-today)
9. [Mutual-exclusivity enforcement design](#9-mutual-exclusivity-enforcement-design)
10. [`nuxt-huia-oidc` rename plan](#10-nuxt-huia-oidc-rename-plan)
11. [`nuxt-huia-headless` module spec](#11-nuxt-huia-headless-module-spec)
12. [Shop sample spec](#12-shop-sample-spec)
13. [E2E test plan](#13-e2e-test-plan)
14. [Docs plan](#14-docs-plan)
15. [Migration sequencing, open questions & risks](#15-migration-sequencing-open-questions--risks)

---

## 1. Overview & goals

Today Huia ships one identity flavor: a full OpenID Connect / OAuth2 provider (`Huia.AspNetCore`)
with a server-rendered Razor Pages account UI, cookie-based interactive sign-in, and OpenIddict as
the protocol engine. This spec adds a **second flavor** — `Huia.Headless` — a bearer-token,
API-only identity surface in the shape of ASP.NET Core's own
[Identity API](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)
(`MapIdentityApi<TUser>()`), extended with **multi-tenancy** and **phone login** — for SPAs and
native apps that want to own their own UI and talk to Huia purely over JSON, with no redirect-based
OAuth dance and no server-rendered pages.

Both flavors sit on top of one **common** layer (`Huia` + `Huia.EntityFrameworkCore`) that owns
everything neither flavor disagrees about: multi-tenancy, the `HuiaUser`/`HuiaRole` Identity model,
per-flow sign-in policy (password / phone lockout & confirmation rules), phone/OTP primitives,
signing-key lifecycle, and the already-token-based `/manage` (self-service) and `/admin` (tenant,
user, role) APIs.

**Goals**

- Six .NET packages: `Huia`, `Huia.EntityFrameworkCore`, `Huia.Headless`,
  `Huia.Headless.EntityFrameworkCore`, `Huia.OpenId`, `Huia.OpenId.EntityFrameworkCore`.
- Two Nuxt modules: `nuxt-huia-oidc` (renamed from `nuxt-huia`), `nuxt-huia-headless` (new).
- `Huia.Headless` and `Huia.OpenId` are **not** meant to be used in the same host, nor are
  `Huia.Headless.EntityFrameworkCore` and `Huia.OpenId.EntityFrameworkCore` — each is a complete,
  independent identity surface on top of the shared common layer, and combining them is refused at
  startup (§9), not merely discouraged.
- A new `Shop` sample (`samples/Shop.Api` + `samples/Shop.App`) proves `Huia.Headless` end-to-end,
  parallel to the existing `Todo.Api`/`Todo.App` OIDC sample.
- E2E coverage and docs for the new flavor at parity with the existing OIDC flavor.

**Non-goals (this phase)**

- Passkeys (WebAuthn) stay `Huia.OpenId`-only. `PasskeyOptions` remains in the common `Huia`
  package as a reserved shape (it has no ASP.NET-Core/EF dependency), but nothing in
  `Huia.Headless` implements it. Revisit once the headless flavor has shipped.
- External (federated / social) login stays `Huia.OpenId`-only. The current implementation is
  explicitly built on the OpenIddict client (`ExternalLoginOptions`'s own doc comment: "implemented
  exclusively through the OpenIddict client, never the classic ASP.NET Core authentication
  handlers"); a headless equivalent would use the classic ASP.NET Core external-auth-handler
  pattern instead and is materially different work. Not requested in scope; call out as future work.
- No feature-parity requirement beyond what's listed in the original requirements — this is not a
  rewrite of `Huia.OpenId`, only a rename plus extraction of what's genuinely shared.
- No behavior change to the OIDC flow itself. `Huia.OpenId` should build, test, and behave exactly
  as `Huia.AspNetCore` does today, modulo namespace/package renames and the code that physically
  moved into the common layer (still reachable exactly as before, just from a different assembly).

---

## 2. Final package/module matrix & dependency graph

| # | Package | Kind | New framework/package dependencies vs. today | Role |
|---|---|---|---|---|
| 1 | `Huia` | .NET lib | **Adds `Microsoft.AspNetCore.App` framework reference** (minimal APIs, Options, DI, MVC/Razor for email templates) + `Finbuckle.MultiTenant` (not its EFCore extension) + `Microsoft.AspNetCore.Identity` (the storage-agnostic core, not `…Identity.EntityFrameworkCore`). **No EF Core. No OpenIddict.** | Multi-tenancy, per-flow Identity policy, `HuiaUserManager`/`HuiaSignInManager`, phone/OTP primitives, signing-key lifecycle abstraction, the bearer-protected `/manage` + `/admin` (users/roles) APIs, eventing, localization, health checks, email sending. |
| 2 | `Huia.EntityFrameworkCore` | .NET lib | Same EF Core dependency as today, scoped down. | Common multi-tenant `HuiaDbContext` base (`HuiaUser`/`HuiaRole`, tenant-scoped indexes, table renames), `HuiaSigningKey` entity. **Ships no migrations**, same as today. |
| 3 | `Huia.Headless` | .NET lib | New: bearer token minting/validation (JWT via `IHuiaKeyRing` from `Huia`), CORS. | The Identity-API-style bearer endpoints: register / login / refresh / logout / phone-login / email-confirm / password-reset / 2FA challenge. Depends on `Huia`. |
| 4 | `Huia.Headless.EntityFrameworkCore` | .NET lib | New. | `HuiaRefreshToken` entity + store (revocable, rotated refresh tokens — a deliberate improvement over the stock Identity API's non-revocable tokens). Depends on `Huia.EntityFrameworkCore`. |
| 5 | `Huia.OpenId` | .NET lib | Same as `Huia.AspNetCore` today, minus what moved to `Huia`. | Renamed from `Huia.AspNetCore`. OpenIddict server + client, the Razor Pages account UI, external login, passkeys, per-client OAuth registration, admin CRUD for clients/scopes/keys, CSP. Depends on `Huia`. |
| 6 | `Huia.OpenId.EntityFrameworkCore` | .NET lib | Same as `Huia.EntityFrameworkCore` today, minus what moved to `Huia.EntityFrameworkCore`. | Renamed from `Huia.EntityFrameworkCore`. OpenIddict entity stores + table renames, passkey entity mapping. Depends on `Huia.EntityFrameworkCore`. |
| 7 | `nuxt-huia-oidc` | npm module | none new | Renamed from `nuxt-huia` (package.json currently says `nuxt-huia`; `src/nuxt/SPEC.md`'s header, which says `huia-nuxt`, is stale and gets corrected in the same pass — see §10). |
| 8 | `nuxt-huia-headless` | npm module | new | Bearer-token client for `Huia.Headless`: stores the access/refresh token pair server-side (same dual-layer session pattern as `nuxt-huia-oidc`, no OAuth redirect dance). |

### Dependency graph

```
                     Huia  ───────────────────────────────┐
                   (no EF, no OpenIddict)                  │
                       │                                   │
        ┌──────────────┴───────────────┐                   │
        ▼                               ▼                   │
Huia.EntityFrameworkCore                │                   │
   (common EF)                          │                   │
        │                               │                   │
   ┌────┴─────┐                   ┌─────┴──────┐            │
   ▼          ▼                   ▼            ▼            │
Huia.OpenId.  Huia.Headless.   Huia.OpenId   Huia.Headless ──┘
EntityFrameworkCore  EntityFrameworkCore   (needs both Huia
   │                    │                   and its own EF
   └───────┬────────────┘                   package at the
           │  (a running host references    app-composition
           │   exactly one EF package,       layer, not as a
           │   never both — §9)              hard PackageReference
           ▼                                 from Huia.Headless
   Huia.OpenId  /  Huia.Headless             itself — see §6.4)
   (app-level composition; a consuming
    project references Huia + Huia.OpenId +
    Huia.OpenId.EntityFrameworkCore, OR
    Huia + Huia.Headless + Huia.Headless.EntityFrameworkCore
    — never a mix of the two families)
```

`Huia.OpenId` and `Huia.Headless` do **not** reference either EF Core package as a
`ProjectReference`/`PackageReference` — same as today's `Huia.AspNetCore`, which ships no EF
provider. The *app* wires `services.AddDbContext<THuiaDbContext>(...)` and calls `AddHuiaOpenId()`
or `AddHuiaHeadless()`; the mutual-exclusivity guard (§9) is a **runtime** check inside those two
extension methods, since there's no compile-time way to stop a project from referencing both
NuGet packages.

---

## 3. Type-by-type migration & rename inventory

Every current public type in `Huia`, `Huia.AspNetCore`, `Huia.EntityFrameworkCore`, classified by
destination. "Common" = moves to (or stays in) `Huia` / `Huia.EntityFrameworkCore`. Namespace
convention: `Huia.AspNetCore.X` → `Huia.OpenId.X` on rename; `Huia.EntityFrameworkCore.X` (the
OIDC-specific parts) → `Huia.OpenId.EntityFrameworkCore.X`; anything landing in the new common
`Huia`/`Huia.EntityFrameworkCore` keeps a `Huia.X` / `Huia.EntityFrameworkCore.X` namespace.

### 3.1 `Huia` (today) → mostly stays common, three carve-outs

| File | Destination | Notes |
|---|---|---|
| `Events/*` (3 files) | **Common** | unchanged |
| `HuiaConstants.cs` | **Split** | see §3.4 — most of it is common; `Cookies.*`, `ApplicationProperties.*`, `Origins.*`, `ClaimTypes.ExternalIdp/ExternalIdToken`, `PasskeyLoginProvider`, `EnrollPromptedTokenName`, `AuthenticationMethods.Passkey` move to a new `HuiaOpenIdConstants` in `Huia.OpenId` |
| `Options/HuiaOptions.cs`, `HuiaOptionsBuilder.cs`, `HuiaOptionsException.cs`, `IHuiaOptionsSection.cs` | **Common** | unchanged |
| `Options/EmailOptions.cs`, `SmsOptions.cs`, `SeedingOptions.cs`, `CleanupOptions.cs`, `TenantBrandingOptions.cs` | **Common** | unchanged |
| `Options/HuiaTenantAuthenticationOptions.cs` | **Common, trimmed** | keeps `UseEmailAndPasswordLogin`/`UsePhoneLogin`/`DisableRegistration`/`IsEmailAndPasswordLoginEnabled`/`IsPhoneLoginEnabled`. **`UseExternalLogin`/`IsExternalLoginEnabled` are removed from this class** — see §3.5 |
| `Options/EmailAndPasswordLoginOptions.cs`, `PhoneOptions.cs` | **Common** | unchanged |
| `Options/TokenLifetimeOptions.cs` | **Common shape** | reused two ways — per-OAuth-client in `Huia.OpenId`, per-tenant in `Huia.Headless` (no "client" concept there) |
| `Options/PasskeyOptions.cs` | **Common, reserved** | no ASP.NET-Core/EF dependency in the type itself; unused by `Huia.Headless` this phase (§1) |
| `Options/TenantOptions.cs` | **Common, trimmed** | keeps `DisplayName`, `Authentication`, `Branding`, `Email`, `Sms`, `Roles`/`AddRoles`. **`Clients`, `Scopes`, `AddClient`, `AddScope` are removed** — become an OpenId-only extension, §3.5 |
| `Options/HuiaClientDescriptor.cs`, `HuiaClientFactories.cs`, `HuiaScopeDescriptor.cs` | **→ `Huia.OpenId.Options`** | OAuth-client/scope concepts, meaningless outside OpenIddict |
| `Options/ExternalLoginOptions.cs` (incl. `ExternalProviderRegistration`) | **→ `Huia.OpenId.Options`** | per its own doc comment, built exclusively on the OpenIddict client |
| `Options/Enums.cs` | **Split** | `ClientKind`, `ExternalProviderKind` → `Huia.OpenId.Options`; `CaptchaMode`, `PasskeyUserVerification`, `PasskeyAuthenticatorAttachment` → stay common (used by phone-login CAPTCHA and the reserved `PasskeyOptions` respectively) |

### 3.2 `Huia.EntityFrameworkCore` (today) → split

| File | Destination | Notes |
|---|---|---|
| `HuiaDbContext.cs` | **Split into a common base + OpenId derivation** | see §5.1 / §8.2 |
| `Entities/HuiaUser.cs`, `HuiaRole.cs` | **→ common `Huia.EntityFrameworkCore.Entities`** | unchanged |
| `Entities/HuiaSigningKey.cs` | **→ common `Huia.EntityFrameworkCore.Entities`** | **moved from OpenId-only to common** — rationale in §3.6 |
| `Multitenancy/HuiaTenantInfo.cs`, `TenantAccessorExtensions.cs` | **→ `Huia.Multitenancy`** (the core `Huia` package, not `Huia.EntityFrameworkCore`) | neither type has an EF Core dependency — only `Finbuckle.MultiTenant.Abstractions` — so they belong in the zero-EF common package per the "move multi-tenancy to `Huia`" requirement |
| `Stores/HuiaOpenIddictApplicationStore.cs`, `HuiaOpenIddictScopeStore.cs` | **→ `Huia.OpenId.EntityFrameworkCore.Stores`** | OpenIddict-specific |

### 3.3 `Huia.AspNetCore` (today) → split three ways

**→ common `Huia`** (namespace `Huia.AspNetCore.X` → `Huia.X`):

| File | New home |
|---|---|
| `Authorization/HuiaAuthorizationPolicyBuilderExtensions.cs` | `Huia.Authorization` |
| `Configuration/HuiaAuthorizationConfiguration.cs`, `HuiaFlowIdentityConfiguration.cs`, `HuiaIdentityConfiguration.cs`, `HuiaMultiTenancyConfiguration.cs` | `Huia.Configuration` |
| `HealthChecks/HuiaHealthChecksConfiguration.cs`, `HuiaReadinessHealthCheck.cs` | `Huia.HealthChecks` |
| `Eventing/ChannelHuiaEventPublisher.cs`, `HuiaEventingExtensions.cs` | `Huia.Eventing` |
| `Identity/HuiaAuthFlow.cs`, `HuiaFlowIdentity.cs`, `HuiaFlowIdentityOptions.cs`, `HuiaUserType.cs`, `HuiaSignInManager.cs`, `HuiaUserManager.cs`, `HuiaRoleSeeder.cs` | `Huia.Identity` |
| `Services/OtpService.cs`, `OtpHashing.cs`, `OtpRateLimiter.cs`, `PendingPhoneSignup.cs`, `PhoneLoginRateLimiter.cs`, `PhoneNumberService.cs`, `CountryCatalog.cs`, `SmsSender.cs` | `Huia.Services` |
| `Endpoints/ManageEndpoints.cs` | `Huia.Endpoints` — **minus** the `GET/DELETE external-logins` handlers, which move to `Huia.OpenId` (§3.5) |
| `Endpoints/AdminEndpoints.cs`, `AdminEndpoints.Roles.cs` | `Huia.Endpoints` |
| `Endpoints/AdminEndpoints.Crud.cs` | **Split**: user CRUD stays `Huia.Endpoints`; client/scope/key CRUD moves to a new `Huia.OpenId`-side file, §3.5 |
| `Endpoints/HuiaEndpointRouteBuilderExtensions.cs` | **Split**: `MapHuiaHealthChecks`, `MapHuiaManageEndpoints`, `MapHuiaAdminEndpoints` (user/role part), `MapHuiaHome` stay common as `Huia.Endpoints.HuiaEndpointRouteBuilderExtensions`; the OIDC-specific mapping moves into `Huia.OpenId`'s own extension class (§8.1) |
| `DependencyInjection/HuiaServiceCollectionExtensions.cs` (`AddHuia`) | **Split** — common subset stays; see §4.4 |
| `DependencyInjection/HuiaApplicationBuilderExtensions.cs` (`UseHuia`) | **Split** — common subset stays; see §4.5 |
| `DependencyInjection/IHuiaBuilder.cs` | `Huia.DependencyInjection` |
| `Localization/HuiaLocalization.cs`, `UiLocalesRequestCultureProvider.cs` | `Huia.Localization` |
| `Emails/EmailModel.cs`, `HuiaEmailSender.cs`, `RazorEmailRenderer.cs` | `Huia.Emails` — **plus** the actual `.cshtml` email templates, currently compiled into `Huia.AspNetCore`'s Razor Class Library, need to move with them so both flavors can render the same confirm-email/reset-password/OTP emails |
| `SharedResource.cs` | `Huia` (localization resource marker) |
| `Keys/*` (7 files: `HuiaKeyBootstrapper`, `HuiaKeyLifecycleService`, `HuiaKeyManagementConfiguration`, `HuiaKeyRing`, `HuiaSigningKeyFactory`, `HuiaSigningKeyMaterial`, `IHuiaKeyProtector`, `IHuiaKeyRing`, `Jobs/KeyLifecycleJobs`) | `Huia.Keys` | **moved from OpenId-only to common** — both flavors mint signed bearer tokens and need key rotation; rationale in §3.6 |
| `Security/HuiaSecurityHeadersMiddleware.cs`, `HuiaSecurityHeadersOptions.cs`, `HuiaSecurityHeadersMarker.cs` | **Trimmed, stays common** | keeps HSTS, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`. **CSP-with-nonce is removed from the common middleware** — meaningless for a JSON API — and re-added by `Huia.OpenId` (§3.5) |

**→ `Huia.OpenId`** (renamed `Huia.AspNetCore.X` → `Huia.OpenId.X`, unchanged content unless noted):

| File | Notes |
|---|---|
| `Areas/Identity/Pages/Account/**` (17 `.cshtml.cs` files + their `.cshtml`) | the whole Razor Pages account UI |
| `Configuration/HuiaCookieConfiguration.cs` | interactive-cookie wiring (`huia.auth`, `huia.2fa`, per-tenant cookie names) — meaningless for a bearer-only flavor |
| `Endpoints/ConnectEndpoints.cs`, `ExternalEndpoints.cs`, `PasskeyEndpoints.cs` | OIDC protocol + external login + passkey endpoints |
| `Flows/AuthFlowState.cs`, `TenantClientHome.cs`, `IReturnUrlProtector.cs` (+ its `ReturnUrlProtector` impl, currently registered by `HuiaServiceCollectionExtensions`/`HuiaUiConfiguration`) | interactive redirect-flow state; headless has no server-side redirect flow to protect |
| `Identity/HuiaPasskeyRegistrar.cs` | passkeys stay OpenId-only (§1) |
| `OpenIddict/*` (all 8 files) | obviously |
| `UI/HuiaAccountPageModel.cs`, `HuiaUiConfiguration.cs`, `HuiaUiMarker.cs`, `RetryAfterText.cs` | the `AddHuiaUi()` opt-in |
| `Security/HuiaCspNonce.cs` | CSP nonce generation, re-attached to the OpenId `AddHuiaSecurityHeaders()` |
| New file: `Endpoints/AdminEndpoints.OpenId.cs` | the client/scope/key CRUD carved out of `AdminEndpoints.Crud.cs` |
| New file: `Endpoints/ManageEndpoints.OpenId.cs` | the `GET/DELETE external-logins` handlers carved out of `ManageEndpoints.cs` |

### 3.4 `HuiaConstants` split

| Member | Destination |
|---|---|
| `ConfigurationSection`, `PasswordlessLoginProvider`, `OtpTokenName`, `Policies.Api`, `Roles.Administrator`, `ClaimTypes.Tenant`, `ClaimTypes.AuthenticationMethod`, `ClaimTypes.GivenName`, `ClaimTypes.FamilyName`, `AuthenticationMethods.Password`, `AuthenticationMethods.Sms` | stays in `Huia.HuiaConstants` |
| `PasskeyLoginProvider`, `EnrollPromptedTokenName`, `AuthenticationMethods.Passkey` | → `Huia.OpenId.HuiaOpenIdConstants` (passkeys are OpenId-only, §1) |
| `Cookies.Authentication`, `Cookies.Flow`, `Cookies.TwoFactorUser`, `Cookies.AntiForgery` | → `Huia.OpenId.HuiaOpenIdConstants` (interactive-cookie names) |
| `ClaimTypes.ExternalIdp`, `ClaimTypes.ExternalIdToken` | → `Huia.OpenId.HuiaOpenIdConstants` (external login) |
| `ApplicationProperties.*`, `Origins.*` | → `Huia.OpenId.HuiaOpenIdConstants` (OpenIddict application `Properties` dictionary keys) |

### 3.5 Design wrinkles that need new code, not just a move

1. **`HuiaTenantAuthenticationOptions.UseExternalLogin`** currently lives on the common
   authentication-options class but is entirely OpenIddict-client-backed. Recommendation: remove
   it from the common class and add it back as an `Huia.OpenId`-contributed extension method that
   operates on `TenantOptions` via `InternalsVisibleTo` (mirroring how `Huia.AspNetCore` already
   reads `Authentication.EmailAndPassword`/`.Phone` through internal accessors today). Call sites
   become `tenant.AddHuiaOpenId(openid => openid.Authentication.UseExternalLogin(...))` rather than
   `tenant.Authentication.UseExternalLogin(...)` — see the extension-configuration pattern below.
2. **`TenantOptions.Clients`/`Scopes`/`AddClient`/`AddScope`** are OAuth/OIDC-only. Recommendation:
   introduce an extension-configuration pattern on `TenantOptions`, e.g.:

   ```csharp
   tenant.AddHuiaOpenId(openid =>
   {
       openid.AddServerSideWebApplication("acme-web", "secret", c => { ... });
       openid.AddScope("orders:read");
       openid.Authentication.UseExternalLogin(ext => ext.AddGoogle(id, secret));
   });

   tenant.AddHuiaHeadless(headless =>
   {
       headless.AllowedOrigins.Add("https://shop.example.com");
       headless.AccessToken.Lifetime = TimeSpan.FromMinutes(15);
       headless.RefreshToken.Lifetime = TimeSpan.FromDays(30);
   });
   ```

   `TenantOptions` gains a small, typed extension slot (e.g. `IDictionary<Type, IHuiaOptionsSection>
   Extensions` plus a protected `GetOrAddExtension<T>()` helper) that `Huia.OpenId` and
   `Huia.Headless` each populate with their own `HuiaOpenIdTenantOptions` /
   `HuiaHeadlessTenantOptions`, both validated through the same `IHuiaOptionsSection` walk
   `HuiaOptions.Validate()` already does. This is the mechanism that also makes §9's
   mutual-exclusivity check trivial: if `Extensions` contains an OpenId entry and a Headless
   builder tries to register, or vice versa, that's the same signal used for the runtime guard.
3. **The `Huia:Api` bearer policy's authentication scheme is currently pinned to OpenIddict
   validation** (`ManageEndpoints`'s policy pins `OpenIddictValidationAspNetCoreDefaults.Scheme`).
   Since `ManageEndpoints`/`AdminEndpoints` move to the common `Huia` package, which references
   neither OpenIddict nor a bearer-token library, the policy must be defined against a **named
   constant scheme** that each flavor's `AddHuiaOpenId()`/`AddHuiaHeadless()` binds at
   registration time (`AddAuthentication().AddPolicyScheme(HuiaConstants.Schemes.Api, ...)` forwarding
   to whichever concrete scheme that flavor registers — OpenIddict validation for `Huia.OpenId`,
   `IdentityConstants.BearerScheme` (or a custom scheme) for `Huia.Headless`). Both flavors must
   produce a principal with the same claims shape (`sub`, `tenant`, `role`) for the common
   `/manage`/`/admin` endpoint code to keep working unmodified — call out as an explicit contract
   test in §13.

### 3.6 Two deliberate re-scopings beyond a pure rename

- **`Keys/*` + `HuiaSigningKey` move from OpenId-only to common.** Both flavors mint signed bearer
  tokens (OpenIddict-issued JWTs for `Huia.OpenId`, hand-minted JWTs for `Huia.Headless` — §6.3),
  both need per-tenant rotating keys, and duplicating `IHuiaKeyRing` + the four Quartz lifecycle
  jobs + the `HuiaSigningKey` entity into two packages would be exactly the kind of drift this
  restructure exists to avoid. `Huia.OpenId`'s `HuiaTenantSigningKeyHandler`/
  `HuiaTenantTokenValidationHandler`/`HuiaTenantJwksHandler` (OpenIddict event-handler glue) stay
  OpenId-only; only the underlying key-ring service and its storage move.
- **`Huia` gains a `Microsoft.AspNetCore.App` framework reference**, superseding the constraint in
  the current `src/dotnet/SPEC.md` §2 ("A build Target fails the compile if an ASP.NET Core / EF
  Core `PackageReference` is added"). This is a breaking change to that stated invariant and is
  the load-bearing decision behind this whole spec: "move manage/admin APIs into `Huia`" is not
  possible without ASP.NET Core minimal APIs. `Huia` still takes **zero EF Core dependency** — its
  Identity surface (`UserManager<HuiaUser>`/`SignInManager<HuiaUser>`/`RoleManager<HuiaRole>`) is
  the storage-agnostic `Microsoft.AspNetCore.Identity` core, not
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore`; the concrete store only appears once a host
  adds `Huia.EntityFrameworkCore`. Flagged again as an explicit decision point in §15 — worth a
  deliberate go/no-go before implementation starts, since it changes a documented architectural
  invariant.

---

## 4. `Huia` (common) surface spec

### 4.1 Package shape

`net10.0`, `Microsoft.AspNetCore.App` framework reference, `Finbuckle.MultiTenant`,
`Microsoft.AspNetCore.Identity` (core, not the EF flavor), `MailKit`/`MimeKit` (email),
`libphonenumber-csharp` (phone), `Microsoft.Extensions.Caching.Hybrid`, `Quartz` (key rotation).
No `Microsoft.EntityFrameworkCore.*`, no `OpenIddict.*`.

### 4.2 Namespace map

`Huia` (options/root), `Huia.Options`, `Huia.Events`, `Huia.Multitenancy`, `Huia.Identity`,
`Huia.Services` (phone/OTP), `Huia.Keys`, `Huia.Endpoints`, `Huia.Emails`, `Huia.Localization`,
`Huia.HealthChecks`, `Huia.Authorization`, `Huia.Configuration`, `Huia.Security`,
`Huia.DependencyInjection`, `Huia.Eventing`.

### 4.3 Options tree (trimmed)

```
HuiaOptions
├─ Issuer, PublicUrl, DisableTransportSecurityRequirement
├─ Email, Sms, Keys : KeyManagementOptions, Cleanup
└─ Tenants : IDictionary<string, TenantOptions>
   └─ TenantOptions
      ├─ DisplayName, Branding, Email?, Sms?, Roles/AddRoles
      ├─ Authentication : HuiaTenantAuthenticationOptions
      │  ├─ UseEmailAndPasswordLogin(...) / IsEmailAndPasswordLoginEnabled
      │  ├─ UsePhoneLogin(...) / IsPhoneLoginEnabled
      │  └─ DisableRegistration()
      └─ Extensions : per-flavor slot, populated by tenant.AddHuiaOpenId(...) / tenant.AddHuiaHeadless(...)
```

`Options.Validate()` walks `Extensions` the same way it walks `Clients`/`Scopes` today, so a
flavor's tenant options get the same "collect everything, throw once" validation behavior.

### 4.4 `AddHuia()` — common DI surface

```csharp
public static IHuiaBuilder AddHuia(this IServiceCollection services, Action<HuiaOptionsBuilder> configure)
```

Registers (unchanged behavior from today's `AddHuia`, minus what's now flavor-specific):
multi-tenancy, `AddIdentity<HuiaUser, HuiaRole>()` + `HuiaUserManager`/`HuiaSignInManager`,
per-tenant `IdentityOptions` projection, per-flow named `IdentityOptions` (§5.6 of
`src/dotnet/SPEC.md`, unchanged), localization, eventing, phone/OTP services + rate limiters, key
management (`Huia.Keys`, moved here per §3.6), data protection, hybrid cache, routing,
authorization policy scaffolding, health checks. **No longer registers**: cookie authentication
scheme, OpenIddict, the account UI, the return-URL protector, the CSP-nonce middleware marker.

Every flavor's own `AddHuiaOpenId()`/`AddHuiaHeadless()` **requires** `AddHuia()` to have run first
(same "call `AddHuiaXyz` before this" guard pattern already used for `HuiaDbContext`
registration), and **registers the caller's chosen authentication scheme** behind the
`HuiaConstants.Policies.Api` policy scheme (§3.5 point 3).

### 4.5 `UseHuia()` — common middleware

```csharp
public static IApplicationBuilder UseHuia(this IApplicationBuilder app)
```

Same order as today minus `UseAuthentication()`: exception handler → status-code pages → request
localization → `UseMultiTenant()` → security headers (HSTS/`X-Content-Type-Options`/
`Referrer-Policy`/`Permissions-Policy` only, no CSP) → static files → routing → **(flavor inserts
`UseAuthentication()` here via its own `UseHuiaOpenId()`/`UseHuiaHeadless()` wrapper, which calls
`UseHuia()` internally then adds its own bit)** → authorization.

### 4.6 `/manage` and `/admin` (user/role) — unchanged behavior, new home

`MapHuiaManageEndpoints()` (profile, password, email+confirm, phone+confirm+delete — **minus**
external-logins, §3.3) and `MapHuiaAdminEndpoints()` (tenants; users — **minus** clients/scopes/
keys, §3.3; roles) move into `Huia.Endpoints`, guarded by the same `HuiaConstants.Policies.Api`
(manage) / `RequireTenants("master").RequireRole(Administrator)` (admin) policies as today. Both
flavors' apps call the exact same `app.MapHuiaEndpoints()` (still defined in `Huia`, still calling
`MapHuiaManageEndpoints`/`MapHuiaAdminEndpoints`/`MapHuiaHealthChecks`/`MapHuiaHome`) — the only
difference is which authentication scheme is behind the policy.

---

## 5. `Huia.EntityFrameworkCore` (common) surface spec

### 5.1 `HuiaDbContext` base

```csharp
namespace Huia.EntityFrameworkCore;

public class HuiaDbContext : MultiTenantIdentityDbContext<HuiaUser, HuiaRole, string>
{
    public HuiaDbContext(IMultiTenantContextAccessor accessor, DbContextOptions<HuiaDbContext> options)
        : base(accessor, options) { }

    public DbSet<HuiaSigningKey> SigningKeys => Set<HuiaSigningKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        RenameIdentityTables(builder);      // HuiaUsers / HuiaRoles / HuiaUserRoles / ...
        ApplyTenantScopedIndexes(builder);  // {TenantId, Normalized*} composite indexes
        ConfigureSigningKeys(builder);      // HuiaSigningKeys table
    }
}
```

This is exactly today's `HuiaDbContext` **minus** `builder.UseOpenIddict()`, `RenameOpenIddictTables`,
and `ConfigurePasskeys` (passkeys stay OpenId-only, so their entity mapping stays in
`Huia.OpenId.EntityFrameworkCore` too — see §8.2). A consuming app never instantiates
`Huia.EntityFrameworkCore.HuiaDbContext` directly; it always uses the flavor-specific derived
context (§7.1, §8.2) so exactly one derivation's tables ever get created. `SigningKeys` moves here
because `HuiaSigningKey` is common now (§3.6).

### 5.2 Entities

`HuiaUser : IdentityUser<string>`, `HuiaRole : IdentityRole<string>` — unchanged. `HuiaSigningKey`
— unchanged, just relocated (table `HuiaSigningKeys`, status enum, tenant-scoped, no
`IsMultiTenant()` since key jobs run tenant-agnostic — same as today).

### 5.3 What does **not** move here

`HuiaTenantInfo` and the `IMultiTenantContextAccessor` extension methods (`CurrentTenantId()` /
`RequireCurrentTenantId()`) move to `Huia.Multitenancy` in the **core** `Huia` package (§3.2) —
they have no EF dependency, so putting them in the EF package would force a Finbuckle-EF dependency
onto code that doesn't need it (e.g. `Huia.Headless`'s token-minting code, which needs
`CurrentTenantId()` but not EF Core).

---

## 6. `Huia.Headless` surface spec

### 6.1 Reference model

Mirrors ASP.NET Core's own `MapIdentityApi<TUser>()`
([docs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0))
for the *session-bootstrap* endpoints only — register / login / refresh / logout / confirm-email /
resend-confirmation / forgot-password / reset-password / 2FA challenge-response during login — and
deliberately does **not** re-implement `/manage/2fa` or `/manage/info`, because the common
`/manage/*` surface (§4.6) is already bearer-token-based and works unmodified once
`Huia.Headless` registers its scheme behind the `Huia:Api` policy. Two deviations from the stock
template, both deliberate:

1. **Multi-tenant**: every route sits under `/{tenant}/identity/...` via the same Finbuckle
   base-path resolution `Huia` already provides — no separate multi-tenancy work needed in this
   package.
2. **Phone login**: `Huia.Headless` adds phone/OTP start+verify endpoints that don't exist in the
   stock template, reusing the same `IOtpService`/`IPhoneNumberService`/`IPendingPhoneSignup`
   primitives `Huia.OpenId`'s Razor Pages already use (now common, §3.3).
3. **Revocable refresh tokens**: the stock Identity API's bearer tokens are self-contained and
   *not* revocable server-side (a documented limitation). `Huia.Headless` persists refresh tokens
   (`Huia.Headless.EntityFrameworkCore`, §7) so logout and admin-initiated revocation actually
   invalidate a session — consistent with `Huia.OpenId`'s OpenIddict-backed refresh-token rotation.

### 6.2 Endpoints

| Route (under `/{tenant}`) | Method | Auth | Notes |
|---|---|---|---|
| `/identity/register` | POST | anon | email+password; 404/disabled per `Authentication.EmailAndPasswordLoginOptions.AllowSelfServiceRegistration` |
| `/identity/login` | POST | anon | `{ email, password }` → `{ accessToken, refreshToken, expiresIn }`, or `{ requiresTwoFactor: true }` |
| `/identity/login` (2FA resubmit) | POST | anon | same route, `{ email, password, twoFactorCode }` or `twoFactorRecoveryCode` |
| `/identity/phone/login/start` | POST | anon | `{ phoneNumber }` → triggers OTP SMS; same rate limits as the Razor `Login`/`VerifyOtp` pages |
| `/identity/phone/login/verify` | POST | anon | `{ phoneNumber, code }` → token pair, or `{ requiresProfileCompletion: true, provisionalToken }` on first-ever sign-in when auto-provisioning is on |
| `/identity/phone/complete-profile` | POST | provisional bearer | `{ firstName, lastName }` → finalizes the auto-provisioned account, returns the real token pair (headless equivalent of the Razor `CompleteProfile` page) |
| `/identity/refresh` | POST | anon (refresh token in body) | rotates the refresh token (§7.2); reuse of an already-rotated token revokes the whole token family |
| `/identity/logout` | POST | bearer | revokes the presented refresh token family |
| `/identity/confirmEmail` | GET | anon | `?userId=&code=`, same semantics as the stock template |
| `/identity/resendConfirmationEmail` | POST | anon | |
| `/identity/forgotPassword` | POST | anon | |
| `/identity/resetPassword` | POST | anon | |
| `/manage/*` | (as §4.6) | bearer | reused unmodified from `Huia` |

### 6.3 Token format

JWTs, minted directly (not via OpenIddict — there's no authorization server here, just a token
issuer) using `IHuiaKeyRing.GetActiveKeyAsync(tenantId)` (moved to common, §3.6) for the signing
key, so a resource API can validate a Huia.Headless-issued token exactly like a
Huia.OpenId-issued one: fetch `{Issuer}/{tenant}/.well-known/jwks`, validate against the tenant's
published key set, check `iss`/`aud`/`exp`. `Huia.Headless` exposes its own minimal JWKS endpoint
(reusing the same `IHuiaKeyRing`; there's no OpenIddict discovery document, so no
`/.well-known/openid-configuration`). Claims: `sub`, `tenant`, `role[]`, `amr` — the same shape
`Huia.OpenId` produces, so `/manage`/`/admin` work unmodified (§3.5 point 3).

### 6.4 DI & options

```csharp
tenant.AddHuiaHeadless(headless =>
{
    headless.AllowedOrigins.Add("https://shop.example.com");     // CORS — SPA is cross-origin by default
    headless.AccessToken.Lifetime = TimeSpan.FromMinutes(15);
    headless.RefreshToken.Lifetime = TimeSpan.FromDays(30);
    headless.RefreshToken.SlidingExpiration = true;
    // RotateOnUse is not configurable — always on, see §7.2
});

builder.Services.AddDbContext<HuiaHeadlessDbContext>(o => o.UseNpgsql(cs));
builder.Services.AddHuia(huia => { ... })
    .AddHuiaHeadless();   // registers the bearer scheme behind Huia:Api, CORS, token minting

app.UseHuiaHeadless();    // UseHuia() + UseAuthentication() (bearer scheme) + UseCors()
app.MapHuiaEndpoints();   // common: /manage, /admin, /health
app.MapHuiaHeadlessEndpoints();   // register/login/refresh/logout/phone/confirm/reset
```

---

## 7. `Huia.Headless.EntityFrameworkCore` surface spec

### 7.1 `HuiaHeadlessDbContext`

```csharp
namespace Huia.Headless.EntityFrameworkCore;

public class HuiaHeadlessDbContext : Huia.EntityFrameworkCore.HuiaDbContext
{
    public DbSet<HuiaRefreshToken> RefreshTokens => Set<HuiaRefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<HuiaRefreshToken>(b =>
        {
            b.ToTable("HuiaRefreshTokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();   // SHA-256(token), never store raw
            b.HasIndex(t => new { t.TenantId, t.TokenHash }).IsUnique();
            b.HasIndex(t => new { t.TenantId, t.FamilyId });               // revoke-whole-family lookup
        });
    }
}
```

### 7.2 `HuiaRefreshToken`

`Id`, `TenantId`, `UserId`, `TokenHash`, `FamilyId` (all tokens descended from one login share a
family id), `IssuedAt`, `ExpiresAt`, `RevokedAt?`, `ReplacedByTokenHash?`. **Rotation-on-use is
always on** (every `/identity/refresh` call issues a new token and marks the presented one
`ReplacedByTokenHash`); presenting an already-`RevokedAt`-stamped token revokes the entire
`FamilyId` — standard refresh-token-theft detection, matching the security posture
`Huia.OpenId`/OpenIddict already has with its own refresh-token rotation.

### 7.3 Store

`IHuiaHeadlessRefreshTokenStore` (`Issue`, `Rotate`, `RevokeFamily`, `FindByHash`) + one EF
implementation. Kept behind an interface so `Huia.Headless`'s token-minting code has no direct EF
dependency — mirrors how `Huia.OpenId`'s `HuiaClientSeeder` talks to OpenIddict's own abstracted
manager rather than the EF store directly.

---

## 8. `Huia.OpenId` / `Huia.OpenId.EntityFrameworkCore` — delta from today

### 8.1 `Huia.OpenId` (renamed `Huia.AspNetCore`)

Purely mechanical for ~80% of the package (namespace `Huia.AspNetCore.*` → `Huia.OpenId.*`,
assembly/package id rename, `PackageReference`/`ProjectReference` updates across `samples/*` and
`tests/*`). The non-mechanical part is exactly the carve-outs in §3.3/§3.5:

- `AddHuia()` → `AddHuiaOpenId()`: registers OpenIddict server+client, key management wiring
  against the now-common `Huia.Keys` (`IHuiaKeyRing`), cookie auth (`HuiaCookieConfiguration`),
  CSP-nonce security headers, the return-URL protector, and binds the OpenIddict validation scheme
  behind `HuiaConstants.Policies.Api` (§3.5 point 3). Everything else it used to do now happens in
  the common `AddHuia()` it requires first.
- `UseHuia()` → `UseHuiaOpenId()`: calls the common `UseHuia()`, inserts `UseAuthentication()`
  (cookie + OpenIddict validation schemes), same as today's order otherwise.
- `MapHuiaEndpoints()` → `MapHuiaOpenIdEndpoints()`: maps `/connect/*`, Razor Pages, external login,
  passkeys, on top of the common `app.MapHuiaEndpoints()` (§4.6).
- `AdminEndpoints.Crud.cs` / `ManageEndpoints.cs` split per §3.3 — client/scope/key admin and
  external-logins management become `Huia.OpenId`-side additive endpoint groups
  (`MapHuiaOpenIdAdminEndpoints()`, folded into `MapHuiaOpenIdEndpoints()`).
- `HuiaTenantAuthenticationOptions.UseExternalLogin` and `TenantOptions.Clients`/`Scopes` become
  `Huia.OpenId`-contributed extension members on the common option types via the
  extension-configuration pattern (§3.5 point 2) — this is the one place callers' `Program.cs`
  changes shape (`tenant.Authentication.UseExternalLogin(...)` →
  `tenant.AddHuiaOpenId(openid => openid.Authentication.UseExternalLogin(...))`,
  `tenant.AddClient(...)` → `tenant.AddHuiaOpenId(openid => openid.AddClient(...))`) — flag this as
  a **breaking API change** callers of the current `Huia.AspNetCore` need to migrate through, worth
  a release note.

### 8.2 `Huia.OpenId.EntityFrameworkCore` (renamed `Huia.EntityFrameworkCore`)

```csharp
namespace Huia.OpenId.EntityFrameworkCore;

public class HuiaOpenIdDbContext : Huia.EntityFrameworkCore.HuiaDbContext
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        RenameOpenIddictTables(builder);   // HuiaApplications / HuiaAuthorizations / HuiaScopes / HuiaTokens
        ConfigurePasskeys(builder);        // HuiaUserPasskeys — stays OpenId-only, §1
    }
}
```

`HuiaOpenIddictApplicationStore`/`HuiaOpenIddictScopeStore` — unchanged, relocated. Nothing else
about the OpenIddict entity model changes.

---

## 9. Mutual-exclusivity enforcement design

Two independent guards, both structured the same way, both throwing `InvalidOperationException`
with an actionable message rather than failing silently or half-registering:

1. **DI-time guard** — `AddHuiaOpenId()` and `AddHuiaHeadless()` each stamp a private marker service
   (`services.TryAddSingleton<HuiaOpenIdMarker>()` / `HuiaHeadlessMarker>()`) on the same
   `IServiceCollection` `AddHuia()` set up, and each **checks for the other's marker before
   registering anything**:

   ```csharp
   public static IHuiaBuilder AddHuiaHeadless(this IHuiaBuilder builder, ...)
   {
       if (builder.Services.Any(d => d.ServiceType == typeof(HuiaOpenIdMarker)))
       {
           throw new InvalidOperationException(
               "AddHuiaHeadless() cannot be combined with AddHuiaOpenId() in the same host. " +
               "Huia.Headless and Huia.OpenId are independent identity surfaces — pick one.");
       }
       ...
   }
   ```

   Order-independent (whichever is called second throws), and catches the case even though nothing
   stops a project from carrying both `PackageReference`s.

2. **Options-tree guard** — mirrors it at the tenant level: `TenantOptions.Extensions` (§3.5
   point 2) can hold at most one of a `HuiaOpenIdTenantOptions` / `HuiaHeadlessTenantOptions` per
   tenant; `tenant.AddHuiaOpenId(...)` / `tenant.AddHuiaHeadless(...)` each throw
   `HuiaOptionsException` up front if the other's extension is already present on that
   `TenantOptions` instance. This catches the narrower case of a shared `HuiaOptions` tree being
   handed to both flavors' builders (unlikely given guard 1, but cheap to also check at the level
   the requirement was stated at — "not meant to be used together").

3. **EF Core side** — `Huia.Headless.EntityFrameworkCore`'s `HuiaHeadlessDbContext` and
   `Huia.OpenId.EntityFrameworkCore`'s `HuiaOpenIdDbContext` are two different `DbContext` types
   registered under two different `DbContextOptions<T>`, so there's no shared registration point to
   guard the way `AddHuia()`/`AddHuiaXyz()` do. The guard here is instead: `AddHuiaHeadless()`
   requires `DbContextOptions<HuiaHeadlessDbContext>` to be registered (same "you must register the
   DbContext first" pattern `AddHuia()` already uses for the base `HuiaDbContext`, §4.4), and
   **refuses if `DbContextOptions<HuiaOpenIdDbContext>` is also registered** (and vice versa) —
   this is the practical signal that a host is trying to stand up both EF flavors' tables in the
   same database/`DbContext` graph.

---

## 10. `nuxt-huia-oidc` rename plan

**Current state, verified**: `src/nuxt/package.json` `"name"` is `nuxt-huia` (not `huia-nuxt` as
`src/nuxt/SPEC.md`'s own header currently claims — that doc is stale and gets corrected in the same
commit as the rename, not left inconsistent).

- npm package name: `nuxt-huia` → `nuxt-huia-oidc`.
- Config key: keep `huiaAuth` (no functional reason to rename it — a config-key rename is pure
  churn for every consumer; the *package* name is what's signaling "this is the OIDC one" now that
  a second module exists).
- Directory: `src/nuxt/` stays as-is (it's already the only Nuxt package directory at that path
  today); if `src/nuxt-headless/` is added alongside for the new module (§11), consider whether
  `src/nuxt/` should become `src/nuxt-oidc/` for symmetry — flag as an open naming question, not a
  strong requirement either way.
- Update: `src/nuxt/SPEC.md` (title + package line + stray `huia-nuxt`/`nuxt-oidc-auth`
  references), `src/nuxt/README.md`, `docs/nuxt/*`, every consumer's `modules: [...]` array
  (`samples/Huia.AdminUI`, `samples/Todo.App`), CI job names (`nuxt-module` → `nuxt-huia-oidc`, or
  keep generic), `Huia.AppHost`/`FrontEndStackFixture` env var prefixes stay `NUXT_HUIA_AUTH_*`
  (tied to the config key, not the package name, so unaffected).
- No behavior change — this is a pure rename, same as `Huia.AspNetCore` → `Huia.OpenId`.

---

## 11. `nuxt-huia-headless` module spec

Parallel structure to `nuxt-huia-oidc` (§3 of `src/nuxt/SPEC.md`), same dual-layer principle
(browser never sees a token) but a materially simpler protocol — no PAR, no PKCE, no authorization
redirect, no discovery document:

| Layer | Holds | Notes |
|---|---|---|
| Nitro Storage, keyed by opaque session id | `accessToken`, `refreshToken`, expiries, user claims | same pattern as `nuxt-huia-oidc`'s `TokenRecord`, minus `idToken`/`scope`-from-OIDC concerns |
| Encrypted, auto-chunked cookie | session id + display claims | identical mechanism (`iron-webcrypto`, `__Host-` prefix, chunking) — **the cookie/session/refresh/storage utility code (`cookie.ts`, `storage.ts`, session get/set/clear, the single-flight+soft-lock refresh guard) is close enough to `nuxt-huia-oidc`'s to justify factoring into a small shared private workspace package** rather than duplicating it — flag as an open question in §15 (Nuxt has no equivalent of the dotnet-side common `Huia` package today) |

**Server routes** (config key `huiaHeadlessAuth`, mirroring `huiaAuth`'s shape):

- `POST /auth/headless/register`, `/login`, `/logout` — direct passthrough to `Huia.Headless`'s
  `/identity/*` endpoints (form/JSON, not a redirect — no `sendRedirect`, the Nitro route returns
  JSON or sets the session and returns `{ ok: true }` for the SPA to react to).
- `POST /auth/headless/phone/login/start`, `/auth/headless/phone/login/verify` — phone login.
- `POST /auth/headless/refresh` — invoked by the same transparent early-refresh logic
  `nuxt-huia-oidc` already has (`ensureFreshTokens`), just calling `Huia.Headless`'s
  `/identity/refresh` instead of an OIDC token-endpoint refresh grant. Refresh-token rotation
  (§7.2) means the stored `refreshToken` **must** be replaced on every refresh, not just the access
  token — a behavioral difference from `nuxt-huia-oidc` worth a unit test of its own.
- `GET /api/_auth/session` — same shape as `nuxt-huia-oidc`'s.

**Composables**: `useUserSession()`, `useAuth()` (register/login/logout/phone-login helpers) —
same names/shapes as `nuxt-huia-oidc` so a consuming app's UI code barely differs between the two
modules; `getAccessToken(event)` server util for attaching `Authorization: Bearer` to upstream API
calls, identical role to today's.

**What's absent vs. `nuxt-huia-oidc`**: no PAR config, no `allowedAuthParams`/`extraAuthParams`
passthrough, no RP-initiated logout redirect (headless logout is just a local API call — no
upstream session to end), no `par`/discovery-related config block.

---

## 12. Shop sample spec

`samples/Shop.Api` (ASP.NET Core minimal API) + `samples/Shop.App` (Nuxt 4), parallel to
`Todo.Api`/`Todo.App`, deliberately minimal — this is an auth reference sample, not a real
storefront:

- **`Shop.Api`**: `AddHuia()` + `AddHuiaHeadless()` + `HuiaHeadlessDbContext`, a `shop` tenant, a
  handful of resource endpoints (`GET /products`, `GET /cart`, `POST /cart/items`,
  `POST /checkout`) protected by the bearer token `Huia.Headless` issues, validated the same way
  `Huia.OpenId`-protected resource APIs already validate tokens (JWKS + `iss`/`aud`/`exp` — no
  Huia-specific SDK needed, standard `AddJwtBearer` against `{Issuer}/{tenant}/.well-known/jwks`).
  In-memory or SQLite catalog/cart data — the point is exercising auth, not building commerce
  logic.
- **`Shop.App`**: Nuxt 4 + `nuxt-huia-headless`, a product listing, a cart page behind
  `definePageMeta({ middleware: 'auth' })`, a login/register flow (email+password and phone),
  calling `Shop.Api` with `getAccessToken(event)`-attached bearer tokens — mirrors `Todo.App`'s
  structure closely enough that a reader comparing the two samples side-by-side sees exactly what
  differs between the OIDC and headless integration patterns.
- Wired into `Huia.AppHost` alongside the existing `Todo.Api`/`Todo.App`/`Huia.IdentityServer`
  services, its own ports, its own `NUXT_HUIA_HEADLESS_AUTH_*` env var prefix.

---

## 13. E2E test plan

New Playwright specs in `tests/Huia.E2ETests`, mirroring the existing `FrontEndAuthE2ETests`/
`HuiaNuxtE2ETests` structure (skip-tolerant, `[Trait("Category","E2E")]`):

- `ShopApiE2ETests` (or an addition to `Huia.IntegrationTests`/`Huia.Tests.PenTest` as
  appropriate) — register, login, refresh (including rotation — reusing an old refresh token must
  fail and revoke the family), logout-then-refresh-fails, phone login start+verify+auto-provision,
  2FA challenge-response, tenant isolation (a token minted for `shop` tenant rejected by a `master`
  tenant-protected route and vice versa — the headless equivalent of the existing
  `HuiaTenantTokenValidationHandler` cross-tenant rejection test).
- `ShopAppE2ETests` — drives `Shop.App` through Playwright: sign up with email, sign in, add to
  cart, checkout, sign out, sign in with phone, session survives reload (SSR hydration, same
  assertion shape as `HuiaNuxtE2ETests`'s token-free-session test), transparent refresh across an
  expired-but-refreshable access token.
- **Contract test** (new, not sample-specific): one test asserting `Huia.OpenId`- and
  `Huia.Headless`-issued tokens produce **identical claims shape** for the same conceptual
  identity, and that the common `/manage`/`/admin` endpoint code works against both — this is the
  test that actually proves the §3.5 point 3 design holds, not just that each flavor works in
  isolation.
- **Mutual-exclusivity test** (new) — a unit test per guard in §9: `AddHuiaHeadless()` after
  `AddHuiaOpenId()` throws (and vice versa); `tenant.AddHuiaHeadless()` after
  `tenant.AddHuiaOpenId()` on the same `TenantOptions` throws; both EF `DbContextOptions<T>`
  registered together throws.
- CI: new `e2e` sub-job (or extend the existing one) builds `Shop.App`'s `.output`, same pattern
  as the existing Nuxt playground/Todo.App build step.

---

## 14. Docs plan

`docs/dotnet/*` restructures from 3-package to 6-package coverage: a new top-level split between
"common" (`Huia`, `Huia.EntityFrameworkCore`), "headless" (`Huia.Headless`,
`Huia.Headless.EntityFrameworkCore`), and "OpenId" (`Huia.OpenId`, `Huia.OpenId.EntityFrameworkCore`)
sidebar groups. `docs/nuxt/*` gets a parallel split (`nuxt-huia-oidc` / `nuxt-huia-headless`).
New pages: a "Choosing a flavor" guide (OpenId vs. Headless — when you want a full OAuth2 provider
serving third-party RPs vs. when you just want your own SPA to authenticate against your own API),
a `Shop` sample walkthrough (`docs/guide/shop-sample.md`, alongside the existing
`docs/guide/admin-ui.md`). `docs/architecture/request-flow.md` gets a second diagram for the
headless bearer-token flow next to the existing authorization-code diagram. Every `docs/dotnet/*`
and `docs/nuxt/*` page that currently says `Huia.AspNetCore`/`Huia.EntityFrameworkCore`/
`nuxt-huia` needs a pass for the renamed identifiers — treat this as a straightforward
find-and-fix once the rename lands, not a rewrite.

---

## 15. Migration sequencing, open questions & risks

### 15.1 Suggested sequencing

Build on the pattern already used for the `src/dotnet`/`src/nuxt` restructure (see the
`huia-monorepo-restructure-2026-09` history: move tracked files individually, run
`dotnet build-server shutdown` between steps since the C# language server holds `bin`/`obj`
handles on Windows and `git mv` on a directory fails "Permission denied", and watch for the IDE
stripping `<ProjectReference>` lines on freshly-moved projects):

1. **Extract the common layer first, with the OpenId rename following as a mechanical step**:
   create `Huia`'s new namespaces/files by moving (not copying) the common-classified types out of
   today's `Huia`/`Huia.AspNetCore`/`Huia.EntityFrameworkCore`; get `Huia.AspNetCore` (still under
   its current name) building again against the trimmed common package. This isolates the risky,
   judgment-heavy part (the §3 classification) from the purely mechanical rename.
2. **Rename `Huia.AspNetCore` → `Huia.OpenId` and `Huia.EntityFrameworkCore` → `Huia.OpenId.EntityFrameworkCore`**
   (project files, namespaces, solution references, every consumer) — mechanical, high file-count,
   low judgment.
3. **Build `Huia.Headless` + `Huia.Headless.EntityFrameworkCore`** on top of the now-stable common
   layer — genuinely new code, no rename risk to worry about in parallel.
4. **Build the `Shop` sample** against `Huia.Headless` — the sample is also the first real
   end-to-end proof the common-layer split actually works for a second flavor.
5. **Nuxt**: rename `nuxt-huia` → `nuxt-huia-oidc` (mechanical), then build `nuxt-huia-headless`
   (new) and `Shop.App`.
6. **E2E + docs** last, once the shape of everything above has stopped moving.

Rebuild + rerun the full test suite after each numbered step, same verification discipline as the
original 6-phase build (`dotnet build` + fix + that phase's tests before moving on).

### 15.2 Open questions (need a decision before/while implementing)

- **§3.6**: does the `Huia` package really gain an `Microsoft.AspNetCore.App` framework reference?
  This is the single highest-leverage decision in this spec — everything about where
  manage/admin/email/phone code lives follows from it. The alternative (keep `Huia` dependency-free
  and instead give `Huia.EntityFrameworkCore` the ASP.NET-Core-aware common code, since it already
  takes a framework dependency) is viable too but means the common layer can no longer be used
  without EF Core, which contradicts "move common functionality to Huia" reading naturally as "the
  smallest common package."
- **§3.5 point 2**: the `TenantOptions.Extensions` typed-slot pattern is a new mechanism not
  present anywhere in the codebase today — confirm it's the preferred shape over, say, each flavor
  shipping its own root-level `HuiaOpenIdOptions`/`HuiaHeadlessOptions` with its own
  `Tenants[id].Extra` dictionary keyed by tenant id (less type-safe, no new mechanism needed).
- **§11**: whether cookie/session/refresh Nuxt runtime code is shared between the two modules via a
  private workspace package, or duplicated. No precedent either way in this repo.
- **§10**: whether `src/nuxt/` becomes `src/nuxt-oidc/` for symmetry with a new `src/nuxt-headless/`
  — purely a naming/discoverability call, no functional impact.
- **Config key** for `Huia.Headless` on the .NET side — this spec assumes
  `tenant.AddHuiaHeadless(...)` mirrors `tenant.AddHuiaOpenId(...)`'s shape exactly; confirm no
  desire for a different verb (`UseHuiaHeadless`, matching `Use*` used elsewhere for sign-in
  method toggles, vs. `Add*` used for tenant/client registration) before locking the public API.

### 15.3 Risks

- **Breaking API change for existing `Huia.AspNetCore` callers** (§8.1) —
  `tenant.Authentication.UseExternalLogin(...)`/`tenant.AddClient(...)` move under
  `tenant.AddHuiaOpenId(...)`. Since nothing has shipped to external consumers yet (greenfield,
  per [[huia-build-scope]]), this is low-risk now and only gets more expensive to make later.
- **`HuiaConstants` split (§3.4) is a source-breaking change** for any code pattern-matching on
  the old flat constant set — mechanical to fix, but every reference needs to be found, not just
  moved file-by-file.
- **Windows file-move lock hazard** (git mv + `dotnet.exe` design-time build holding `bin`/`obj`
  handles) already bit the previous restructure once (`huia-monorepo-restructure-2026-09`) —
  budget for it again given the much larger file count moving this time (all of
  `Huia.AspNetCore`'s ~90 files get touched one way or another).
- **Windows Nuxt `npm i --legacy-peer-deps` / arborist crash on the nuxt devtools peer graph** —
  already a known gotcha ([[huia-monorepo-restructure-2026-09]]) that will recur for
  `nuxt-huia-headless`'s fresh `package.json`.
