# Changelog

All notable changes to Huia are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **`Huia`** — `HuiaTenantAuthenticationOptions.DisableRegistration()` turns off every anonymous
  account-creation path in one call: `EmailAndPassword.AllowSelfServiceRegistration` and, when the
  phone flow is enabled, `Phone.AllowAutoProvisioning` (left untouched — not implicitly enabling the
  phone flow — when it was never used). `TenantOptions.DisableRegistration()` now delegates to it
  instead of only turning off self-service email/password registration.
- **`Huia.AspNetCore`** — flow-aware Identity managers. `HuiaAuthFlow`
  (`Default` / `EmailAndPasswordLogin` / `PhoneLogin` / `ExternalLogin`) plus
  `IHuiaFlowIdentityFactory`, which hands each flow a `HuiaUserManager` + new `HuiaSignInManager` pair
  reading that flow's own named `IdentityOptions` instance. `AddHuiaFlowIdentity` registers one
  `IdentityOptions` per flow, each built entirely from that flow's own options object
  (`EmailAndPasswordLoginOptions` / `PhoneOptions`) — nothing is shared across flows, including
  lockout (see *Changed*) — so the password path gates on a confirmed email with its own brute-force
  ceiling, the phone path on a confirmed phone with its own unrelated ceiling, and the external path
  on neither, with no bespoke branching. The interactive sign-in entry points (`Login`, `VerifyOtp`,
  `CompleteProfile`, `Register`, `ForgotPassword` / `ResetPassword` / `ConfirmEmail`,
  `ExternalEndpoints`, `/connect/token`) resolve managers through the factory; `/manage`, `/admin`
  and seeding keep the default managers.

### Changed

- **`Huia.AspNetCore`** — **Breaking.** the custom `HuiaUserConfirmation : IUserConfirmation<HuiaUser>`
  is removed (and its `services.Replace`). The confirmed-email vs confirmed-phone split is now a
  property of the per-flow `IdentityOptions` (see *Added*) with the stock `DefaultUserConfirmation`.
  The process-global `AddIdentity` registration is now a fixed, non-computed baseline — it no longer
  derives a "least restrictive across tenants" password / lockout fallback, or unions
  `SignIn.RequireConfirmedEmail` — every real request is served by the per-tenant or per-flow
  projection instead.
- **`Huia` / `Huia.AspNetCore`** — **Breaking.** lockout is per **flow**, per tenant, not per tenant.
  `TenantOptions.Lockout` / `TenantLockoutOptions` are removed; `MaxFailedAccessAttempts` /
  `LockoutDuration` / `AllowedForNewUsers` moved onto `EmailAndPasswordLoginOptions` and `PhoneOptions`
  — each flow's own ceiling, independent of the other. Migration: `tenant.Lockout.MaxFailedAccessAttempts
  = n` → the matching flow's `.MaxFailedAccessAttempts = n` inside its `Use*` configure lambda.

### Fixed

- **`Huia.AspNetCore`** — the "forgot password" page now carries `returnUrl` through to its "Sign in"
  link, so starting password recovery in the middle of an OAuth authorize request no longer strands the
  relying party ("invalid oauth flow") after the reset.
- **`Huia.AspNetCore`** — the "you've signed in from this number very recently" limit is now checked on
  the login page **before** a one-time code is sent, not after it has been verified. The limiter gains
  a non-consuming `CanRecordLogin` for the pre-check; the permit is still consumed only on a completed
  sign-in.
- **`Huia.AspNetCore`** — signing out of a session that was established through an external provider now
  also ends the upstream provider's session (RP-initiated end-session via the OpenIddict client + a new
  `signout-callback-oidc` endpoint). The provider must register `{tenant}/signout-callback-oidc` as a
  post-logout redirect URI (wired for the `Huia.External` sample).
- **`Huia.AspNetCore`** — the account-UI error alert no longer sits flush against the text above it
  (the word-wrap fix had also zeroed the vertical rhythm); the profile-completion page gains native
  client-side validation (`wwwroot/js/form-validate.js`, `required` + `asp-validation-for` spans) so an
  empty-name submit shows a message instead of silently re-rendering.

### Changed

- **`Huia`** — **Breaking.** `HuiaTenantAuthenticationOptions.EmailAndPassword` / `.Phone` / `.External`
  (formerly `.Password` / `.Passwordless`) are not public. Configure them exclusively through
  `tenant.Authentication.UseEmailAndPasswordLogin(p => …)`, `.UsePhoneLogin(phone => …)` and
  `.UseExternalLogin(ext => …)` — three top-level calls, no `Passwordless` umbrella grouping phone
  and external together any more; the tenant exposes derived `IsEmailAndPasswordLoginEnabled` /
  `IsPhoneLoginEnabled` / `IsExternalLoginEnabled` bools for reads. `PasswordFlowOptions` is renamed
  `EmailAndPasswordLoginOptions`; `PhoneLoginOptions` is renamed `PhoneOptions`;
  `PasswordlessFlowOptions` is removed. Migration:
  `tenant.Authentication.UsePasswordFlow(p => …)` → `UseEmailAndPasswordLogin(p => …)`;
  `tenant.Authentication.UsePasswordlessFlow(pwl => pwl.UsePhoneLogin(…))` → `UsePhoneLogin(…)`
  (same for `UseExternalLogin`); `tenant.Authentication.Password.RequireConfirmedEmail = false` →
  `tenant.Authentication.UseEmailAndPasswordLogin(p => p.RequireConfirmedEmail = false)`.
- **`Huia`** — **Breaking.** `DefaultCountry` moved from `PasswordlessFlowOptions` to
  `PhoneOptions` (it is a phone-only concern). Migration:
  `pwl.DefaultCountry = "SA"` → `UsePhoneLogin(phone => phone.DefaultCountry = "SA")`.
- **`Huia`** — **Breaking.** `ExternalLoginOptions.LinkExistingAccountsByEmail()` →
  `EnableAccountsLinking()`; the backing flag `LinkToExistingConfirmedEmail` → `AccountLinkingEnabled`.
- **`Huia`** — **Breaking.** `HuiaClientDescriptor.RequirePushedAuthorizationRequests` is now a fluent
  method, not a settable `bool` property (matching `DisableRegistration()` /
  `EnableAccountsLinking()`). Migration: `client.RequirePushedAuthorizationRequests = true` →
  `client.RequirePushedAuthorizationRequests()`. The backing property is
  `RequiresPushedAuthorizationRequests`; the admin API's `ClientWriteRequest` JSON field is
  unchanged.
- **`Huia` / `Huia.AspNetCore`** — password-complexity policy is now per tenant.
  `EmailAndPasswordLoginOptions` gains `RequireDigit` / `RequireLowercase` / `RequireUppercase` /
  `RequireNonAlphanumeric` / `RequiredUniqueChars` / `RequireUniqueEmail`.
  `AddHuiaPerTenantIdentityOptions` projects the password fields onto the default `IdentityOptions`
  for the resolved tenant (via Finbuckle `ConfigurePerTenant` + a scoped `IOptions<IdentityOptions>`
  bridge so the DI-injected `UserManager` / `SignInManager` observe them). Lockout is per flow, not
  per tenant — see the per-flow-identity entries above.
- **`Huia.AspNetCore`** — the account UI renders the tenant's branding: the `_Layout` shows
  `Branding.LogoUrl`, `Branding.FaviconUrl` and `Branding.AccentColor`, and a Terms / Privacy /
  Support footer from `Branding.TermsUrl` / `PrivacyUrl` / `SupportUrl` (previously defined but
  unused). The email and phone sign-in forms carry a one-line description
  (`Login.EmailDescription` / `Login.PhoneDescription`), and the phone country picker wraps above
  the number on narrow screens.
- **`Huia.AspNetCore`** — after an interactive sign-in reached with no OAuth flow in progress (no
  `returnUrl`), and after an interactive sign-out, the user is sent to the tenant's first registered
  client home / post-logout URL instead of the identity server's own tenant root. `SignedIn` gains a
  "Continue to {app}" link. The `/connect/logout` client-less fallback shares the same resolver
  (`TenantClientHome`).
- **`Huia.AspNetCore`** — the committed account-UI assets under `wwwroot/` are now regenerated by an
  MSBuild target (`BuildAccountUiAssets` in `Huia.AspNetCore.csproj`) that runs the Tailwind CLI and
  copies the vendored files out of `node_modules`, replacing `build-assets.mjs`. It runs during the
  build when `node_modules` is present (or `-p:ForceAccountUiAssets=true`); a Node-less build leaves
  the committed output untouched.
- **`Huia.AppHost` sample** — Mailpit is wired through the `CommunityToolkit.Aspire.Hosting.MailPit`
  integration (`AddMailPit` + `WithDataVolume`, endpoints `smtp` / `http`) instead of a raw
  `AddContainer`.

- **`Huia.AspNetCore`** — the `/admin` list rows for clients and scopes now carry an `origin` field:
  `static` (seeded from the options tree by `HuiaClientSeeder` / `HuiaScopeSeeder`) or `dynamic`
  (created through `POST /admin/scopes`). `PUT` / `DELETE /admin/scopes/{name}` now return
  `409 Conflict` for a `static` scope — code-defined scopes are read-only over the admin API. The
  marker lives in the OpenIddict entity's `Properties` under `huia:origin` (`HuiaConstants.Origins`);
  a missing marker is treated as `static`.

- **`Huia.EntityFrameworkCore`** — tenant isolation is now enforced by Finbuckle instead of bespoke
  stores. `HuiaDbContext` derives from `MultiTenantIdentityDbContext<HuiaUser, HuiaRole, string>`
  (new package refs `Finbuckle.MultiTenant.EntityFrameworkCore` +
  `Finbuckle.MultiTenant.Identity.EntityFrameworkCore`): every Identity entity carries a `TenantId`,
  reads are restricted by a global query filter, and `SaveChanges` throws `MultiTenantException` on a
  tenant-less or cross-tenant write. **Breaking**: the constructor is now
  `HuiaDbContext(IMultiTenantContextAccessor, DbContextOptions<HuiaDbContext>)`; the custom
  `HuiaUserStore` / `HuiaRoleStore`, `AddHuiaStores()`, and the `IHuiaTenantContext` /
  `IHuiaAmbientTenantProvider` abstraction are removed. Read the current tenant with the
  `IMultiTenantContextAccessor.CurrentTenantId()` / `RequireCurrentTenantId()` extensions; scope
  seeding / background work with `HuiaTenantScope.Enter(serviceProvider, tenantId)`. `HuiaTenantInfo`
  moved to the `Huia.EntityFrameworkCore.Multitenancy` namespace. Adopters on a real EF Core provider
  must add one migration (`dotnet ef migrations add PerTenantIdentity`) for the new `TenantId` shadow
  columns and composite indexes on the Identity join/claim tables.
- **`Huia.AspNetCore`** — interactive login sessions are now bound to their tenant:
  `WithPerTenantAuthentication()` rejects an authentication ticket replayed under a different tenant,
  and each tenant gets its own cookie name (`huia.auth.{tenant}` / `huia.2fa.{tenant}`) so one
  browser can hold several tenants' sessions at once.
- **`Huia`** — the password sign-in flow is now opt-in and symmetric with the passwordless umbrella:
  `TenantAuthenticationOptions.UsePasswordFlow(...)` enables it, and `PasswordFlowOptions.Enabled`
  now defaults to `false`. **Breaking**: a tenant that previously relied on the implicit
  `Password.Enabled = true` must call `tenant.Authentication.UsePasswordFlow()` (optionally with a
  configure callback for `MinimumLength` etc.).
- **`Huia.AspNetCore`** — `RouteOptions.LowercaseUrls` is enabled, so generated links (including the
  Razor account-UI page links) are lower-case.
- **`Huia.AspNetCore`** — the security-headers `form-action` CSP directive now also allows the
  configured OAuth clients' redirect/home origins and the external providers' authority origins, so a
  browser can complete an interactive sign-in that redirects the login form POST off-origin. Extend it
  further via `HuiaSecurityHeadersOptions.AdditionalFormActionSrc`.
- **`Huia.AspNetCore`** — `MapHuiaHome()` no longer redirects a tenant root (`/{tenant}/`) back to
  itself: an anonymous caller is sent to sign-in and an authenticated one to a new `SignedIn` account
  page (`/identity/account/signedin`). This removes the redirect loop seen after sign-out and the blank
  page seen after signing in directly at the identity server — for example completing an external
  (partner) sign-up with no client application in the flow.
- **`Huia.AspNetCore`** — account-UI forms have a consistent vertical rhythm again: Basecoat's `.form`
  class carries no layout, so `Register` / `CompleteProfile` / `ForgotPassword` / `ResetPassword` (and
  the login partials) rendered their fields and submit button flush against each other. The card body
  and the form are now stacked with an even gap, and the card's top padding is no longer doubled.
- **`Huia.AspNetCore`** — `/connect/logout` without a resolvable client (for example a relying party
  that sends an empty `id_token_hint`) now falls back to a configured client's post-logout / home URL
  for the tenant instead of the identity server's own tenant root.
- **`Huia.AspNetCore`** — the redesigned one-time-code verification page (`VerifyOtp`): tightened
  spacing, a masked-number intro line, a clearer break before the submit button, and a "send a new
  code" control with a visible cooldown countdown (`PhoneLoginOptions.ResendCooldown`, default 30s).
- **`Huia.AspNetCore`** — account-UI alerts (`.alert` / `.alert-destructive`) no longer wrap one word
  per line: Basecoat's alert grid placed the bare message text in its zero-width icon column, so it is
  now rendered as a normal block.
- **`Todo.App` / `Huia.IdentityServer` samples** — in Development the stub SMS sender writes the
  plaintext one-time code to the log, so a phone sign-in can be completed without an SMS provider.
- **`Huia`** — `TenantFeatureOptions` (`tenant.Features`) is removed; its members move onto the flow
  they belong to. `RequireConfirmedEmail`, `RequireUniqueEmail` and `AllowSelfServiceRegistration`
  are now on `Authentication.Password` (`PasswordFlowOptions`); the inert `RequireConfirmedPhoneNumber`
  is dropped. **Breaking**: replace `tenant.Features.X` with `tenant.Authentication.Password.X`.
- **`Huia`** — self-service registration is now **on by default**
  (`PasswordFlowOptions.AllowSelfServiceRegistration = true`); call `tenant.DisableRegistration()` to
  turn it off. The account UI hides the "create an account" link on the login page when registration
  is disabled for the tenant.

- **`Huia.AspNetCore`** — the `/admin/*` API gains full CRUD for users (`GET/POST/PUT/DELETE
  /admin/users[/{id}]`) and clients (`GET/POST/PUT/DELETE /admin/clients[/{id}]`), plus
  `POST /admin/keys`, `GET /admin/keys/{id}`, `POST /admin/keys/{id}/revoke` and
  `DELETE /admin/keys/{id}` for signing keys. Admin-created clients are stamped `huia:origin = dynamic`;
  `PUT`/`DELETE` on a code-seeded ("static") client returns `409`. The `HuiaClientDescriptor` →
  `OpenIddictApplicationDescriptor` translation moved to a shared `HuiaApplicationDescriptorMapper`
  used by both the seeder and the admin API. `samples/Huia.AdminUI` gains create / edit / delete
  forms on the Users, Clients and Keys pages.
- **`Huia.AspNetCore`** — the `/manage/*` self-service API now enforces contact rules by account
  type (new `HuiaUserType` classifier): a password or external account cannot set a phone number
  (`PUT /manage/phone` → 400), and a phone-login account cannot set an email
  (`PUT /manage/email` → 400) or remove its number (`DELETE /manage/phone` → 400). When a phone-login
  account changes its number, the username is moved with it (`SetUserNameAsync`).

### Removed

- **`Huia` / `Huia.AspNetCore`** — the resource-owner-password-credentials (`password`) grant is
  removed: `PasswordFlowOptions.EnableResourceOwnerPasswordGrant`, the token-endpoint password
  branch and `AllowPasswordFlow()` are gone. Use the authorization-code or device-authorization flow.
- **`Huia.AspNetCore`** — the standalone `/identity/account/phonelogin` page is gone; the phone
  sign-in is now the phone tab of the login page (`OnPostPhoneAsync` / `?handler=Phone`). The tabbed
  layout already required JavaScript, so there is no separate no-JS phone page any more.

### Added

- **`Huia.AspNetCore`** — the `/admin/*` API gains per-tenant roles: `GET/POST/PUT/DELETE /admin/roles`
  (a role with members can't be deleted) and `GET/POST/DELETE /admin/users/{id}/roles`. The user list
  and detail now include a `roles` array. `samples/Huia.AdminUI` adds a Roles page and inline role
  assignment on the Users page.
- **`Huia` / `Huia.AspNetCore`** — a signed-in user can link and unlink external sign-in providers.
  The external callback now goes through the standard `IdentityConstants.ExternalScheme` +
  `GetExternalLoginInfoAsync` dispatcher; there is a new `identity/account/externallogins` Razor page
  and `GET` / `DELETE /manage/external-logins` endpoints (the only sign-in method can't be removed).
  A `Huia.External` "partner" is listed on the `Todo.App` profile page.
- **`Huia`** — `ExternalLoginOptions.LinkExistingAccountsByEmail()` (default off): a logged-out
  external sign-in whose verified email matches a confirmed local account is linked to it instead of
  creating a new account. When it is off or the account is ineligible, an email collision is refused
  ("email already registered") rather than silently forking a second account.
- **`Huia` / `Huia.AspNetCore`** — Pushed Authorization Requests (RFC 9126). The server exposes
  `/{tenant}/connect/par` (advertised as `pushed_authorization_request_endpoint`); every interactive
  client may use it, and `HuiaClientDescriptor.RequirePushedAuthorizationRequests` (also a **Require
  PAR** toggle in the admin console / `POST /admin/clients`) makes it mandatory for a client by adding
  the OpenIddict `ft:par` requirement.
- **`Huia.AspNetCore`** — `UseHuia()` now installs `UseStatusCodePagesWithReExecute`, so bare
  `404` / `401` / `403` / `500` responses render a branded page (`Areas/Identity/Pages/Account/Status`,
  new `Status.*` resx keys in en/ar). Requests under `/connect`, `/manage`, `/admin`, `/.well-known`
  and JSON callers get the plain status code with no HTML. OpenIddict's status-code-pages integration
  is left disabled so protocol errors keep their machine-readable responses.
- **CI** — `release.yml` publishes the NuGet packages (`Huia`, `Huia.EntityFrameworkCore`,
  `Huia.AspNetCore` + symbols) on a `v*.*.*` tag or `workflow_dispatch`, with the version derived by
  **MinVer** (CI-gated) and pushed with `NUGET_API_KEY`.
- **`Huia`** — `TenantBrandingOptions.FaviconUrl`, and `HuiaClientDescriptor.ClientUri` / `LogoUri`
  (seeded into the OpenIddict application `Properties` as `huia:client_uri` / `huia:logo_uri`).
- **`Huia.AppHost` sample** — the `Huia.Cli` admin tool is registered as an on-demand resource
  (`WithExplicitStart()` + `WithTerminal()`), pre-wired with `--issuer` / `--tenant master`.
- **`Huia.IdentityServer` / `Huia.External` samples** — every branding option is set for the seeded
  tenants (logo, favicon, accent, terms / privacy / support), with placeholder legal pages served
  from `wwwroot/`.
- **`Huia.AppHost` sample** — Mailpit is wired as an SMTP sink container; the identity server binds
  `Huia:Email` from configuration and sends real mail through it (`aspire run` and CI). The
  `CapturingEmailSender` / `/e2e-mail` fallback is used only when no `Huia:Email:Host` is set. New
  `tests/Huia.E2ETests/MailFlowE2ETests` boots the AppHost via `Aspire.Hosting.Testing` and asserts
  the forgot-password / confirm-email links land in Mailpit and work. See
  [Email testing](docs/guide/email-testing.md).
- **`Huia`** — `PhoneLoginOptions` gains a throttle for *successful* phone sign-ins (distinct from
  code-request throttling): `SuccessfulLoginsPerWindow` / `SuccessfulLoginWindow` (default 1 per two
  minutes) and `SuccessfulLoginsPerDay` (default 5), plus `ResendCooldown` (default 30s) for the
  verification page's "send a new code" countdown. All per tenant.
- **`Huia.AspNetCore`** — `IPhoneLoginRateLimiter` (default in-memory `System.Threading.RateLimiting`
  implementation) enforces the successful-sign-in throttle; `VerifyOtp` shows a localized cooldown or
  daily-limit message when a number is over its limit.
- **`Huia.AspNetCore`** — `UiLocalesRequestCultureProvider` resolves the account-UI culture from the
  OpenID Connect `ui_locales` authorize parameter and persists it in the culture cookie, so a relying
  party can carry its user's language selection into the Huia sign-in pages.
- **`Todo.App` sample** — `@nuxtjs/i18n` (English + Arabic/RTL) with a header language switcher; the
  selected locale is forwarded to Huia via `ui_locales` on the OIDC authorize request.
- **`Huia`** — options tree (root → tenant → password / passwordless umbrella → phone / external /
  email / SMS / token-lifetime / branding / feature / key-management), a dependency-free aggregating
  validator, the six domain events (`UserRegistered`, `UserLoggedIn`, `PasswordChanged`,
  `OtpRequested`, `OtpVerified`, `PhoneChanged`), the `IHuiaEventPublisher` abstraction, the
  `IHuiaTenantContext` multi-tenancy contract, and the client-shape factory methods
  (`AddServerSideWebApplication`, `AddSinglePageApplication`, `AddNativeApplication`, `AddDevice`,
  `AddMachineToMachineApplication`). No ASP.NET Core / EF Core dependency (build-guarded).
- **`Huia.EntityFrameworkCore`** — `HuiaDbContext : IdentityDbContext<HuiaUser, HuiaRole, string>` with
  `UseOpenIddict()`, every table renamed `Huia*`, the default Identity global indexes replaced with
  tenant-scoped composite indexes, the `HuiaSigningKey` entity, and the tenant-scoped `HuiaUserStore`
  / `HuiaRoleStore` / `HuiaOpenIddictApplicationStore`. Provider-agnostic; ships no migrations.
- **`Huia.AspNetCore`** — `AddHuia()` / `UseHuia()` with the load-bearing middleware order; Finbuckle
  base-path multi-tenancy; always-on cookie hardening; opt-in `AddHuiaSecurityHeaders()` CSP layer;
  OpenIddict server + validation + client with the four per-tenant key handlers; the Quartz key
  lifecycle (bootstrap + rotate + activate + retire + delete) with Data-Protection key wrapping;
  the connect endpoints (authorize, token, userinfo, logout, discovery, JWKS) covering
  authorization-code + PKCE, refresh, client-credentials and device; passwordless SMS OTP
  (`IOtpService`, `IPendingPhoneSignup`, `System.Threading.RateLimiting`-backed throttle,
  `IPhoneNumberService` / `ICountryCatalog`); external login via the OpenIddict client; the Razor
  Pages account UI (RCL, en/ar, RTL) with HTML email (`RazorEmailRenderer` + MailKit); the
  self-service `/manage/*` API; and the `/admin/*` endpoints with `MR.AspNetCore.Pagination` keyset
  pagination and the `RequireTenants` / `RequirePresenter` / `RequireRole` / `RequireAudience` policy
  builders; `IHealthCheck`-based probes mapped by `MapHuiaEndpoints()` at `/health/live`
  (liveness) and `/health/ready` (readiness — database reachable and start-up seeding complete);
  per-tenant custom OAuth scopes — declarative (`tenant.AddScope(...)` + `HuiaScopeSeeder`),
  runtime CRUD (`/admin/scopes` GET/POST/PUT/DELETE), isolated by a tenant-filtering
  `HuiaOpenIddictScopeStore`; the device-flow end-user verification page
  (`/identity/account/deviceverification` + the `connect/verify` handler), completing the
  device-authorization grant end to end; and a [Basecoat](https://basecoatui.com)-based restyle of
  the Razor Pages account UI and the HTML email template — a committed Tailwind v4 build
  (`npm run build`), a single tabbed email/phone login page, a segmented one-time-code input,
  country flags (`flag-icons`) on the phone country picker, FontAwesome brand marks on the
  external-login buttons, and `libphonenumber-js` client-side phone validation. Every JS
  enhancement degrades to a working plain-HTML form.
- **Samples** — `Huia.IdentityServer` (real host, Sqlite / Npgsql; the `todo` tenant now also
  federates to `Huia.External` via an `HuiaExternal` OIDC provider, and `master` seeds an
  `huia-cli` device client), `Huia.External` (mock OIDC provider), `Todo.Api` (JwtBearer resource
  server), a .NET Aspire `Huia.AppHost`, `Huia.Cli` (a `System.CommandLine` admin tool that signs in
  with the device-authorization grant — `login` / `logout` / `whoami` / `token` / `scopes list`),
  and two Nuxt 4 front-ends: `Todo.App` (landing + `/tasks` CRUD + `/profile`, with
  `libphonenumber-js` client-side validation on the phone widget) and `Huia.AdminUI` — a sidebar
  admin console covering every `/admin` endpoint: a dashboard, the tenants / users / clients /
  signing-keys lists (keyset pagination, tenant filters, static/dynamic origin badges) and full
  per-tenant scope CRUD (create / edit / delete, with code-defined scopes shown read-only), plus
  `/profile` — both confidential `nuxt-oidc-auth` clients, now built on real
  **shadcn-vue** (`reka-ui`, `shadcn-nuxt`) with **`nuxt-api-party`** proxying every upstream call
  (the access token is injected server-side and never reaches the browser) and **`@nuxtjs/color-mode`**
  with a light/dark toggle (dark by default).
- **Tests** — unit (`Huia.Tests`), in-process + Testcontainers-PostgreSQL integration
  (`Huia.IntegrationTests`, including health-check, scope-seeding, admin scope CRUD and device-flow
  coverage), Playwright E2E (`Huia.E2ETests`) covering the Razor UI, the `Huia.Cli` device flow, and
  both Nuxt front-ends end to end (password / phone / external sign-in and sign-out, forgot-password,
  confirm-email — the fixture boots the whole sample stack out-of-process), the `Huia.AdminUI` console
  driven through `Aspire.Hosting.Testing` (`AdminUiE2ETests` — sign-in, every admin section, and the
  scope create / edit / delete round-trip), and HTTP security assertions (`Huia.Tests.PenTest`).
