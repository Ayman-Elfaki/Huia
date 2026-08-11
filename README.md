# Huia

**Huia** is an OpenIddict-based identity and authentication library for ASP.NET Core applications. It provides a complete identity solution with built-in UI, OAuth 2.0 / OpenID Connect server capabilities, and flexible key management.

## Features

- 🏗️ **OpenIddict Integration** - Full OAuth 2.0 / OpenID Connect authorization server
- 🎨 **Built-in Identity UI** - Razor Pages for login, register, 2FA, password reset, and more
- 🌍 **Localization** - English and Arabic (RTL) out of the box, easily extensible
- 🔐 **Key Management** - Automatic or manual signing/encryption key rotation
- 📦 **EF Core Support** - Ready-to-use Entity Framework Core stores
- 🔧 **Extensible** - Custom store implementations, events, and email providers
- 🚀 **Multiple Client Flows** - SPA, native, server-side web, machine-to-machine, and device authorization

## Installation

```bash
dotnet add package Huia
dotnet add package Huia.EntityFrameworkCore
```

## Quick Start

### 1. Configure Services

```csharp
using Huia;
using Huia.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

builder.Services.AddHuia("https://localhost:5001", huia =>
{
    huia.Branding.Title = "My App";

    // Register client applications
    huia.Applications.AddSinglePageApplication(app =>
    {
        app.SetClientId("my-spa");
        app.AddRedirectUri("https://localhost:3000/callback");
        app.AddPostLogoutRedirectUri("https://localhost:3000");
        app.AllowScopes("my-api");
    });

    // Enable automatic key management
    huia.KeysManagement.UseAutomaticKeyManagement();
})
.WithEntityFrameworkStores<AppDbContext>();
```

### 2. Configure Pipeline

```csharp
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider
        .GetRequiredService<AppDbContext>()
        .Database.MigrateAsync();
}

app.UseHuia();
app.UseAuthentication();
app.UseAuthorization();

app.MapHuiaConnectEndpoints();
app.MapHuiaManageEndpoints();
app.MapRazorPages();

app.Run();

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : HuiaDbContext(options);
```

## Documentation

- [Getting Started](docs/getting-started.md) - Installation and setup guide
- [Architecture](docs/architecture.md) - How Huia works internally
- [Localization](docs/localization.md) - Multi-language support (English/Arabic)
- [Key Management](docs/key-management.md) - Signing and encryption key rotation
- [Custom Stores](docs/custom-store.md) - Implement your own persistence layer

## Supported Client Types

| Method | Client Type | Flow | Secret Required |
|--------|-------------|------|-----------------|
| `AddSinglePageApplication` | SPA (React/Vue/etc.) | Authorization Code + PKCE | No |
| `AddNativeApplication` | Native/Desktop/Mobile | Authorization Code + PKCE | No |
| `AddServerSideWebApplication` | Server-rendered web app | Authorization Code | Yes |
| `AddMachine2Machine` | Service-to-service | Client Credentials | Yes |
| `AddDevice` | Input-constrained devices | Device Authorization | No |

## Endpoints

After calling `app.UseHuia()` and mapping endpoints:

- `/connect/*` - OpenIddict OAuth/OIDC endpoints (authorize, token, userinfo, logout, device)
- `/identity/account/*` - Identity UI pages (login, register, 2FA, password reset)
- `/identity/manage/api/*` - User account management API
- `/identity/admin/api/*` - Admin CRUD for applications, scopes, users, roles

## Samples

The repository includes complete sample applications:

- **Huia.TodoApi** - A Todo CRUD API with Huia authentication
- **Huia.TodoApp** - Next.js frontend consuming the API
- **Huia.AdminUI** - Administrative interface example

Run the samples with .NET Aspire:

```bash
cd samples/Huia.AppHost
dotnet run
```

## Requirements

- .NET 10.0+
- ASP.NET Core
- Entity Framework Core (for `Huia.EntityFrameworkCore`)

## License

Huia is licensed under the [MIT License](LICENSE).

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.
