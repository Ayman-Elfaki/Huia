---
name: huia-dotnet-backend
description: >-
  Use this skill when integrating, configuring, or consuming Huia NuGet packages (Huia.OpenId,
  Huia.Headless, Huia.OpenId.EntityFrameworkCore, Huia.Headless.EntityFrameworkCore) in ASP.NET Core
  applications, configuring tenants, registering OAuth/OIDC clients, or protecting downstream APIs.
---

# Integrating Huia in ASP.NET Core Applications

This guide explains how to consume the Huia library suite in ASP.NET Core 10 applications. Huia provides two primary consumption flavors:
1. **`Huia.OpenId`**: A multi-tenant OpenID Connect / OAuth 2.0 Identity Provider with Razor Pages UI, base-path routing (`/{tenant}/...`), PKCE/PAR, passkeys, and SMS.
2. **`Huia.Headless`**: A single-tenant, pure REST/JSON authentication backend using ASP.NET Core Identity and bearer tokens.
3. **API Resource Servers**: How downstream web APIs validate Huia-issued tokens.

For complete client registration examples and tenant authentication recipes, see [Tenant & Client Configuration Reference](./references/architecture.md).  
For integration testing recipes (e.g., `WebApplicationFactory`), see [Integration Testing Reference](./references/testing.md).

---

## 1. Multi-Tenant Identity Server (`Huia.OpenId`)

### Required Packages
```xml
<PackageReference Include="Huia.OpenId" Version="1.0.0-*" />
<PackageReference Include="Huia.OpenId.EntityFrameworkCore" Version="1.0.0-*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.0" /> <!-- Or Sqlite / SqlServer -->
```

### DbContext Setup
Define a DbContext inheriting from `HuiaDbContext<HuiaUser, HuiaRole, string>`:

```csharp
using Huia.OpenId.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

public class IdentityHuiaDbContext : HuiaDbContext<HuiaUser, HuiaRole, string>
{
    public IdentityHuiaDbContext(DbContextOptions<IdentityHuiaDbContext> options)
        : base(options) { }
}
```

Register EF Core with OpenIddict support in `Program.cs`:
```csharp
builder.Services.AddDbContext<IdentityHuiaDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("huia"));
    options.UseOpenIddict();
});
```

### Identity Server Registration (`Program.cs`)
```csharp
using Huia;
using Huia.OpenId.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Huia.Options;

var huiaBuilder = builder.Services.AddHuiaOpenId(huia =>
{
    huia.UseIssuer("https://id.example.com");
    huia.UsePublicUrl("https://id.example.com");

    if (builder.Environment.IsDevelopment())
    {
        huia.DisableTransportSecurityRequirement();
    }

    // Configure Tenants
    huia.AddTenant("acme", tenant =>
    {
        // Branding
        tenant.Branding.DisplayName = "Acme Portal";
        tenant.Branding.AccentColor = "#4f46e5";

        // Authentication Methods
        tenant.Authentication.UseEmailAndPasswordLogin(pwd =>
        {
            pwd.RequireConfirmedEmail = false;
            pwd.MinimumLength = 8;
        });

        // Passkey / WebAuthn passwordless sign-in
        tenant.Authentication.UsePasskeyLogin();

        // Phone SMS OTP sign-in
        tenant.Authentication.UsePhoneLogin(phone =>
        {
            phone.DefaultCountry = "US";
            phone.AllowAutoProvisioning = true;
        });

        // External Identity Providers (Google, OIDC Federation)
        tenant.Authentication.UseExternalLogin(ext =>
        {
            ext.AddGoogle("google-client-id", "google-client-secret");
            ext.EnableAccountsLinking();
        });

        // Register Server-Side Web App (Next.js / Nuxt / MVC)
        tenant.AddServerSideWebApplication("acme-web", "super-secret-key", client =>
        {
            client.DisplayName = "Acme Web Client";
            client.RedirectUris.Add(new Uri("https://acme.example.com/api/auth/callback"));
            client.PostLogoutRedirectUris.Add(new Uri("https://acme.example.com/"));
            client.RequirePushedAuthorizationRequests(); // RFC 9126 PAR
            client.Token.AccessToken = TimeSpan.FromMinutes(15);
            client.Token.RefreshToken = TimeSpan.FromDays(7);
        });

        // Register Single Page Application (React / Vue / Scalar Docs)
        tenant.AddSinglePageApplication("acme-spa", client =>
        {
            client.DisplayName = "Acme SPA";
            client.RedirectUris.Add(new Uri("https://spa.example.com/callback"));
        });

        // Register Machine-to-Machine Client (Background Workers)
        tenant.AddMachineToMachineApplication("acme-worker", "worker-secret");
    });
});

// Attach Stores, UI, and Security Headers
huiaBuilder
    .AddEntityFrameworkCoreStores<IdentityHuiaDbContext, HuiaUser, HuiaRole>()
    .AddHuiaUi()
    .AddHuiaSecurityHeaders();
```

### Middleware Pipeline & Endpoint Mapping
Order matters. In `Program.cs`:
```csharp
var app = builder.Build();

// 1. Core Huia middleware (localization, Finbuckle multi-tenancy, security headers, auth)
app.UseHuiaOpenId();

// 2. Map standard OIDC protocol + Razor account UI routes (/{tenant}/connect/* and /{tenant}/identity/*)
app.MapHuiaEndpoints();

// 3. Map Administrative Endpoints (user & client management)
app.MapHuiaAdminEndpoints()
   .RequireAuthorization(p => p.RequireTenants("master").RequireRole(HuiaConstants.Roles.Administrator));

// 4. Default home redirect (optional)
app.MapHuiaHome("master");

app.Run();
```

---

## 2. Resource Server / API Protection

Downstream web APIs that receive access tokens issued by Huia should validate them using `OpenIddict.Validation.AspNetCore`:

### Package Required
```xml
<PackageReference Include="OpenIddict.Validation.AspNetCore" Version="6.1.0" />
<PackageReference Include="OpenIddict.Validation.SystemNetHttp" Version="6.1.0" />
```

### API Configuration (`Program.cs`)
```csharp
using OpenIddict.Validation.AspNetCore;

var authority = "https://id.example.com/acme"; // Tenant issuer URL

builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(authority);
        options.UseSystemNetHttp(); // Automatically fetches JWKS and discovery
        options.UseAspNetCore();
    });

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Protected API endpoint
app.MapGet("/api/todos", (ClaimsPrincipal user) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    return Results.Ok(new[] { "Task 1", "Task 2" });
}).RequireAuthorization();

app.Run();
```

---

## 3. Headless Authentication Flavor (`Huia.Headless`)

For single-tenant RESTful APIs that only require pure JSON endpoints without Razor UI or full OIDC protocol overhead:

### Package Required
```xml
<PackageReference Include="Huia.Headless" Version="1.0.0-*" />
<PackageReference Include="Huia.Headless.EntityFrameworkCore" Version="1.0.0-*" />
```

### Configuration (`Program.cs`)
```csharp
builder.Services.AddDbContext<HeadlessDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHuiaHeadless()
    .AddEntityFrameworkCoreStores<HeadlessDbContext, HuiaUser, HuiaRole>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Maps JSON authentication endpoints: /api/auth/login, /api/auth/register, /api/auth/refresh, etc.
app.MapHuiaHeadlessEndpoints();

app.Run();
```
