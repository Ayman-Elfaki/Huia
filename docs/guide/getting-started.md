# Getting started

Huia ships five NuGet packages across two authentication flavors — a multi-tenant OpenID Connect
provider (`Huia.OpenId`) and a single-tenant bearer-token API (`Huia.Headless`) — over a shared core:

| Package | Contents |
|---|---|
| `Huia` | Domain model, options tree, eventing, store abstractions, constants. No EF Core / OpenIddict / Finbuckle dependency. |
| `Huia.OpenId.EntityFrameworkCore` | Generic `HuiaDbContext<TUser,TRole,TId> : MultiTenantIdentityDbContext`, tenant-scoped stores, `AddEntityFrameworkCoreStores<...>()`. Ships no migrations. |
| `Huia.OpenId` | `AddHuiaOpenId()` / `UseHuiaOpenId()`, OpenIddict, the Razor account UI, key jobs. |
| `Huia.Headless.EntityFrameworkCore` | Generic single-tenant `HuiaDbContext<TUser,TRole,TId> : IdentityDbContext<TUser,TRole,string>`, `AddEntityFrameworkCoreStores<...>()` with `MR.AspNetCore.Pagination`. |
| `Huia.Headless` | `AddHuiaHeadless()` / `MapHuiaHeadlessEndpoints()` — store-agnostic, bearer tokens via `MapIdentityApi`, no OpenIddict, no multi-tenancy. |

This page covers `Huia.OpenId`. For the headless flavor, see the
[Shop sample](https://github.com/Ayman-Elfaki/Huia/tree/main/samples/Shop/Shop.Api) and the
[`nuxt-huia-headless`](https://github.com/Ayman-Elfaki/Huia/tree/main/src/nuxt/nuxt-huia-headless)
module it pairs with.

```csharp
builder.Services.AddDbContext<HuiaDbContext>(o => o.UseNpgsql(connectionString).UseOpenIddict());

builder.Services.AddHuiaOpenId(huia =>
{
    huia.UseIssuer("https://id.example.com");
    huia.AddTenant("acme", tenant =>
    {
        tenant.Authentication.UseEmailAndPasswordLogin();
        tenant.AddServerSideWebApplication("acme-web", "secret", client =>
            client.RedirectUris.Add(new Uri("https://acme.example.com/callback")));
    });
})
    .AddEntityFrameworkCoreStores<HuiaDbContext, HuiaUser, HuiaRole>()
    .AddHuiaUi();

var app = builder.Build();
app.UseHuiaOpenId();
app.MapHuiaEndpoints();
```

## Health probes

`MapHuiaEndpoints()` also maps two anonymous probes at the application root:

| Route | Meaning |
|---|---|
| `/health/live` | Process liveness. Runs no checks; `200` whenever the app is up. |
| `/health/ready` | Readiness. `503` until the database is reachable **and** start-up seeding has completed (every configured tenant has a signing key and every configured client exists), `200` afterwards. |

Point an orchestrator's readiness probe (Kubernetes, .NET Aspire, a load balancer) at `/health/ready`.

See [Configuration](/guide/configuration) for the full options tree.
