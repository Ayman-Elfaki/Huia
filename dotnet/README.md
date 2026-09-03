# Huia

Multi-tenant OpenID Connect / OAuth 2.0 Identity Provider library suite for ASP.NET Core 10.

Huia serves multiple isolated tenants over base-path routing (`/{tenant}/...`) and ships as three
NuGet packages:

| Package | Contents |
|---|---|
| `Huia` | Domain model, options tree, eventing abstractions, constants. **No** ASP.NET Core / EF Core dependency. |
| `Huia.EntityFrameworkCore` | `HuiaDbContext`, `Huia*`-renamed entities, tenant-scoped Identity + OpenIddict stores. Provider-agnostic; ships no migrations. |
| `Huia.AspNetCore` | `AddHuia()` / `UseHuia()`, OpenIddict server + client, the Razor Pages account UI, passwordless SMS, key lifecycle jobs, security headers. |

```csharp
builder.Services.AddDbContext<HuiaDbContext>(o => o.UseNpgsql(cs).UseOpenIddict());

builder.Services.AddHuia(huia =>
{
    huia.UseIssuer("https://id.example.com");
    huia.AddTenant("acme", tenant =>
    {
        tenant.Authentication.UsePasswordFlow();
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

```
src/        Huia, Huia.EntityFrameworkCore, Huia.AspNetCore
samples/    Huia.AppHost (Aspire), Huia.IdentityServer, Huia.External, Todo.Api, Todo.App, Huia.AdminUI
tests/      Huia.Tests, Huia.IntegrationTests, Huia.E2ETests, Huia.Tests.PenTest
docs/       VitePress documentation site
```

## Build

```bash
dotnet build Huia.slnx -c Release
dotnet test  Huia.slnx -c Release --filter "Category!=Container&Category!=E2E"
```

The `Container` tests need Docker (Testcontainers PostgreSQL); the `E2E` tests need Playwright
browsers (`pwsh tests/Huia.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium`).
