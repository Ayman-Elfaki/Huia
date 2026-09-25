# Huia

Multi-tenant OpenID Connect / OAuth 2.0 Identity Provider library suite for ASP.NET Core 10.

Huia serves multiple isolated tenants over base-path routing (`/{tenant}/...`) and ships as five
NuGet packages across two authentication flavors:

| Package | Contents |
|---|---|
| `Huia` | Domain model, options tree, eventing abstractions, store abstractions (`IHuiaStore`, `IHuiaOpenIdAdminStore`), constants. **No** ASP.NET Core / EF Core dependency. |
| `Huia.OpenId.EntityFrameworkCore` | Generic `HuiaDbContext<TUser,TRole,TId>`, `Huia*`-renamed entities, tenant-scoped Identity + OpenIddict stores, `AddEntityFrameworkCoreStores<...>()` with keyset pagination. Provider-agnostic; ships no migrations. |
| `Huia.OpenId` | `AddHuiaOpenId()` / `UseHuiaOpenId()`, OpenIddict server + client, the Razor Pages account UI, passwordless SMS, key lifecycle jobs, security headers. |
| `Huia.Headless.EntityFrameworkCore` | Generic single-tenant `HuiaDbContext<TUser,TRole,TId>`, `AddEntityFrameworkCoreStores<...>()` with `MR.AspNetCore.Pagination` keyset and offset pagination. |
| `Huia.Headless` | `AddHuiaHeadless()` / `MapHuiaHeadlessEndpoints()` — store-agnostic, pure JSON authentication endpoints & bearer tokens via `MapIdentityApi`. |

```csharp
builder.Services.AddDbContext<HuiaDbContext<HuiaUser, HuiaRole, string>>(o => o.UseNpgsql(cs).UseOpenIddict());

builder.Services.AddHuiaOpenId(huia =>
{
    huia.UseIssuer("https://id.example.com");
    huia.AddTenant("acme", tenant =>
    {
        tenant.Authentication.UseEmailAndPasswordLogin();
        tenant.AddServerSideWebApplication("acme-web", "secret", client =>
            client.RedirectUris.Add(new Uri("https://acme.example.com/callback")));
    });
}).AddHuiaUi().AddHuiaSecurityHeaders();

var app = builder.Build();
app.UseHuia();          // exception handler -> localization -> multi-tenant -> security headers -> routing -> authn -> authz
app.MapHuiaEndpoints(); // connect + manage + account UI + external login
app.MapHuiaAdministrativeEndpoints()
   .RequireAuthorization(p => p.RequireTenants("master").RequireRole(HuiaConstants.Roles.Administrator));
```

## Highlights

- **Tenant isolation without an ambient filter** — the custom EF Core stores append an explicit
  `TenantId` predicate to every query.
- **Per-tenant signing keys** — each tenant signs with its own rotated RSA key; Quartz jobs drive the
  `pending → active → rotated → retired` lifecycle; private material is wrapped with Data Protection.
- **Flows** — authorization code + PKCE (with optional pushed authorization requests), refresh, client credentials, device code, and
  passwordless SMS one-time codes; external login via the OpenIddict client.
- **Batteries included** — Razor Pages account UI (en/ar, RTL), HTML email, self-service `/manage`
  API, admin API with keyset pagination.

## Layout

This project is `src/dotnet/` inside the Huia monorepo (see the repo-root `README.md`).

```
src/dotnet/
  src/        Huia, Huia.OpenId, Huia.OpenId.EntityFrameworkCore, Huia.Headless, Huia.Headless.EntityFrameworkCore
  tests/      Huia.Tests, Huia.IntegrationTests, Huia.Tests.PenTest
../../samples/  Shared/Huia.AppHost (Aspire), Shared/Huia.External, Shared/Huia.Cli,
                Todo/Todo.Api, Todo/Todo.Nuxt, Todo/Todo.Next, Todo/Todo.Admin, Todo/Todo.IdentityServer,
                Shop/Shop.Api, Shop/Shop.Nuxt, Shop/Shop.Next
../../tests/    Huia.E2ETests   (full-stack Playwright E2E)
../../docs/     VitePress documentation site
```

## Build

```bash
dotnet build Huia.slnx -c Release
dotnet test  Huia.slnx -c Release --filter "Category!=Container&Category!=E2E"
```

The `Container` tests need Docker (Testcontainers PostgreSQL); the `E2E` tests (repo-root
`tests/Huia.E2ETests`) need Playwright browsers
(`pwsh ../../tests/Huia.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium`).
