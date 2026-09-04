# Options reference

The Huia configuration tree is rooted at `HuiaOptions` and bound from the `Huia` configuration
section, configured in code via the `AddHuia(huia => …)` callback, or both. The full tree and its
one-pass validation are specified in [`src/dotnet/SPEC.md` §3](https://github.com/Ayman-Elfaki/Huia/blob/main/src/dotnet/SPEC.md); this page is the practical reference.

## The tree

```
HuiaOptions
├─ Issuer : Uri                                 required — per-tenant issuer is {Issuer}/{tenant}
├─ PublicUrl : Uri?                             absolute-link base for emails sent outside a request
├─ DisableTransportSecurityRequirement : bool   dev / in-process tests only
├─ Email : EmailOptions                         root SMTP; a tenant's Email merges over this
├─ Sms : SmsOptions                             root SMS;  a tenant's Sms merges over this
├─ Keys : KeyManagementOptions                  signing-key lifecycle (EnableBackgroundJobs, rotation windows)
├─ Seeding : SeedingOptions                     PruneRemovedStaticEntities (off by default) — see Pruning below
├─ Cleanup : CleanupOptions                     OpenIddict.Quartz pruning of authorizations/tokens — see Cleanup below
└─ Tenants[id] : TenantOptions
   ├─ DisplayName : string?
   ├─ Authentication : HuiaTenantAuthenticationOptions   — fluent-only, see below; each sign-in method
   │                                                       carries its own lockout policy, there is no
   │                                                       tenant-wide Lockout any more
   ├─ Branding : TenantBrandingOptions                   DisplayName, LogoUrl, FaviconUrl, AccentColor, TermsUrl, PrivacyUrl, SupportUrl
   ├─ Email : EmailOptions?
   ├─ Sms : SmsOptions?
   ├─ Clients : IList<HuiaClientDescriptor>
   ├─ Scopes : IList<HuiaScopeDescriptor>
   └─ Roles : IList<string>                             code-defined ("static") roles — see Roles below
```

## Sign-in methods — fluent only

`tenant.Authentication` exposes **methods, not data properties**. `EmailAndPasswordLoginOptions`,
`PhoneOptions` and `ExternalLoginOptions` are internal; configure them through `UseEmailAndPasswordLogin`
/ `UsePhoneLogin` / `UseExternalLogin` — each is a top-level call, not nested under an umbrella — and
read enablement through the derived bools. Each method's options carry their own **lockout** policy
(`MaxFailedAccessAttempts`, `LockoutDuration`, `AllowedForNewUsers`) — a phone brute-force ceiling has
nothing to do with a password brute-force ceiling, so there is no tenant-wide lockout setting.

```csharp
huia.AddTenant("acme", tenant =>
{
    tenant.Authentication
        .UseEmailAndPasswordLogin(p =>
        {
            p.MinimumLength = 12;              // default 10
            p.RequireDigit = true;            // default true
            p.RequireLowercase = true;
            p.RequireUppercase = true;
            p.RequireNonAlphanumeric = false;
            p.RequiredUniqueChars = 1;
            p.RequireConfirmedEmail = true;   // default true
            p.RequireUniqueEmail = false;
            p.AllowSelfServiceRegistration = true;  // or tenant.Authentication.DisableRegistration()
            p.MaxFailedAccessAttempts = 5;    // this flow's own lockout ceiling
            p.LockoutDuration = TimeSpan.FromMinutes(15);
            p.AllowedForNewUsers = true;
        })
        .UsePhoneLogin(phone =>
        {
            phone.DefaultCountry = "SA";           // ISO 3166-1 alpha-2, for national numbers
            phone.AllowAutoProvisioning = true;    // unknown well-formed number may start a sign-up
            phone.CodeLength = 6;                  // 4–10
            phone.CodeLifetime = TimeSpan.FromMinutes(5);
            phone.MaxVerificationAttempts = 5;
            phone.ResendCooldown = TimeSpan.FromSeconds(30);
            phone.SuccessfulLoginsPerWindow = 1;   // successful-sign-in ceiling per number
            phone.SuccessfulLoginWindow = TimeSpan.FromMinutes(2);
            phone.SuccessfulLoginsPerDay = 5;
            phone.RequireConfirmedPhoneNumber = true;  // default true
            phone.MaxFailedAccessAttempts = 5;         // this flow's own lockout ceiling
        })
        .UseExternalLogin(ext =>
        {
            ext.AddGoogle("client-id", "client-secret");
            ext.AddGitHub("client-id", "client-secret");
            ext.AddMicrosoftAccount("client-id", "client-secret");
            ext.AddOpenIdConnect("Partner", "id", "secret", "https://partner.example/", p =>
            {
                p.DisplayName = "Partner";
                p.Scopes.Add("profile");
                p.Scopes.Add("email");
            });
            ext.EnableAccountsLinking();   // link a verified provider email to an existing confirmed account
        });

    // reads (used by the account UI, the admin API, the OpenIddict client wiring):
    // tenant.Authentication.IsEmailAndPasswordLoginEnabled
    // tenant.Authentication.IsPhoneLoginEnabled
    // tenant.Authentication.IsExternalLoginEnabled
});
```

`tenant.Authentication.DisableRegistration()` turns off every anonymous account-creation path in one
call: `AllowSelfServiceRegistration` and, when the phone flow is enabled, `AllowAutoProvisioning` —
only an administrator can create accounts afterwards. `TenantOptions.DisableRegistration()` is a
shortcut for the same call.

::: tip Migrating from an earlier version
`UsePasswordFlow(p => ...)` → `UseEmailAndPasswordLogin(p => ...)`.
`UsePasswordlessFlow(pwl => pwl.UsePhoneLogin(...))` → `UsePhoneLogin(...)`, called directly on
`tenant.Authentication` (same for `UseExternalLogin`) — the `Passwordless` umbrella is gone.
`tenant.Lockout.MaxFailedAccessAttempts = n` → the matching flow's own
`.MaxFailedAccessAttempts = n` inside its `Use*` lambda (lockout is per sign-in method now, not per
tenant). `tenant.Authentication.Password.RequireConfirmedEmail = false` →
`tenant.Authentication.UseEmailAndPasswordLogin(p => p.RequireConfirmedEmail = false)`.
`pwl.DefaultCountry` moved onto `PhoneOptions` — use `UsePhoneLogin(phone =>
phone.DefaultCountry = "SA")`. `ext.LinkExistingAccountsByEmail()` → `ext.EnableAccountsLinking()`.
:::

## Clients

```csharp
tenant.AddServerSideWebApplication("acme-web", "secret", client =>
{
    client.RedirectUris.Add(new Uri("https://acme.example/callback"));
    client.PostLogoutRedirectUris.Add(new Uri("https://acme.example/"));
    client.HomeUris.Add(new Uri("https://acme.example/"));       // first = sign-out fallback target
    client.Scopes.Add("reports:read");                           // beyond the always-granted openid
    client.RequirePkce = true;                                   // forced on for public clients anyway
    client.RequireConsent = false;
    client.RequirePushedAuthorizationRequests();                 // fluent — adds the ft:par requirement
    client.Token.AccessToken = TimeSpan.FromMinutes(15);         // per-client lifetime overrides
});

tenant.AddSinglePageApplication("acme-spa", client =>            // public, no secret, PKCE forced
    client.RedirectUris.Add(new Uri("https://acme.example/")));
tenant.AddMachineToMachineApplication("acme-worker", "secret");  // client credentials
tenant.AddDevice("acme-cli", client => client.ClientSecret = "secret");  // device authorization
```

`Kind` (`ServerSideWebApplication` | `SinglePageApplication` | `NativeApplication` | `MachineToMachine`)
determines the default grant types and endpoint permissions. A secret is **required** for
`ServerSideWebApplication` / `MachineToMachine` and **forbidden** for the public shapes — validation
enforces this.

## Scopes

```csharp
tenant.AddScope("reports:read", scope =>
{
    scope.DisplayName = "Read reports";
    scope.Description = "Read-only access to the reporting API.";
    scope.Resources.Add("reports-api");     // added to the token audience
});
```

Code-defined ("static") scopes and clients are **read-only in the admin console** and `PUT` / `DELETE`
on them returns `409`. Runtime `POST /admin/{scopes,clients}` always creates a dynamic entity.

## Roles

```csharp
tenant.AddRoles("editor", "beta-tester");
```

Creates each role for the tenant at start-up if it doesn't already exist, stamped "static" like scopes
and clients — **read-only in the admin console**, and `PUT` / `DELETE` on it returns `409`. Only stamped
at creation: a role that already exists under that name (created dynamically before the code declaration
was added) is left alone. Runtime `POST /admin/roles` always creates a dynamic (fully editable) role.

## Pruning removed static entities

```csharp
huia.ConfigureSeeding(seeding => seeding.PruneRemovedStaticEntities = true);
```

Off by default. When on, a static role / scope / client that no longer appears anywhere in the current
options tree — including one whose entire tenant was removed — is **deleted** at the next start-up, not
just left behind. Turning this on means removing a line of code deletes data, so it's an explicit,
per-deployment opt-in rather than always-on behavior.

Deleting a scope or client is always safe (OpenIddict cascades an application's authorizations/tokens,
and nothing references a scope by foreign key — at worst a client still requesting a removed scope gets
`invalid_scope`). A static **role** that still has members is the one exception: it's skipped (and
logged as a warning) rather than force-deleted, so removing a role declaration never silently drops a
user's role assignment.

## Cleanup

```csharp
huia.ConfigureCleanup(cleanup =>
{
    cleanup.EnableBackgroundJobs = true;               // default; turn off in tests
    cleanup.PruneAuthorizations = true;
    cleanup.PruneTokens = true;
    cleanup.MinimumAuthorizationLifespan = TimeSpan.FromDays(14);  // must be >= 10 minutes
    cleanup.MinimumTokenLifespan = TimeSpan.FromDays(14);          // must be >= 10 minutes
    cleanup.MaximumRefireCount = 2;                    // must be >= 0
});
```

Wires up [OpenIddict.Quartz](https://documentation.openiddict.com/), OpenIddict's own package for
pruning orphaned and expired authorizations and tokens, onto Huia's existing Quartz scheduler (the one
that also runs the signing-key lifecycle jobs). The job itself runs hourly with a short random startup
jitter — that interval is fixed by OpenIddict.Quartz and is not configurable; the knobs above only
control *what* it prunes and its own safety guards.

`Cleanup.EnableBackgroundJobs` and `Keys.EnableBackgroundJobs` are independent toggles — turning off key
background jobs does not implicitly turn off cleanup, or vice versa. The `Huia:EnableBackgroundJobs`
config key in `Huia.IdentityServer`'s `Program.cs` is wired to both for convenience; define your own
separate config keys if you need independent control in your own host.

## Validation

`HuiaOptions.Validate()` (also run by `HuiaOptionsBuilder.Build()`) walks the whole tree in one pass
and throws `HuiaOptionsException` carrying **every** problem as
`"Huia:Tenants:acme:Authentication:Phone:DefaultCountry: must be a two-letter upper-case ISO 3166-1
alpha-2 code."`-style messages. Cross-field rules include "at least one sign-in method enabled per
tenant", `PendingSignupLifetime >= CodeLifetime`, and `SuccessfulLoginsPerDay >= SuccessfulLoginsPerWindow`.
