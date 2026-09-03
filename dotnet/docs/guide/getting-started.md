# Getting started

Huia ships as three NuGet packages:

| Package | Contents |
|---|---|
| `Huia` | Domain model, options tree, eventing, constants. No ASP.NET Core / EF Core dependency. |
| `Huia.EntityFrameworkCore` | `HuiaDbContext`, `Huia*` entities, tenant-scoped stores. Provider-agnostic; ships no migrations. |
| `Huia.AspNetCore` | `AddHuia()` / `UseHuia()`, OpenIddict, the Razor account UI, key jobs. |

```csharp
builder.Services.AddDbContext<HuiaDbContext>(o => o.UseNpgsql(connectionString).UseOpenIddict());

builder.Services.AddHuia(huia =>
{
    huia.UseIssuer("https://id.example.com");
    huia.AddTenant("acme", tenant =>
    {
        tenant.Authentication.UsePasswordFlow();
        tenant.AddServerSideWebApplication("acme-web", "secret", client =>
            client.RedirectUris.Add(new Uri("https://acme.example.com/callback")));
    });
}).AddHuiaUi();

var app = builder.Build();
app.UseHuia();
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
