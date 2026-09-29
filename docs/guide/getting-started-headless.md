# Getting Started — Huia.Headless

`Huia.Headless` is a lightweight, single-tenant authentication API for ASP.NET Core applications. Unlike `Huia.OpenId`, which provides OAuth 2.0 / OpenID Connect redirection flows with Razor UI, `Huia.Headless` is a 100% JSON-driven API that issues opaque bearer tokens. It is designed for applications where the frontend owns the login forms and UI directly.

---

## 1. Package Installation

Install the core headless packages:

```bash
dotnet add package Huia.Headless
dotnet add package Huia.Headless.EntityFrameworkCore
```

---

## 2. Configure the Database Context

`Huia.Headless.EntityFrameworkCore` provides a single-tenant `HuiaDbContext` without multi-tenant schema additions:

```csharp
using Huia.Entities;
using Huia.Headless.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

// Register DbContext with your preferred provider (SQLite, PostgreSQL, SQL Server)
builder.Services.AddDbContext<HuiaDbContext<HuiaUser, HuiaRole, string>>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("huia"));
});
```

---

## 3. Register Headless Services

Configure `AddHuiaHeadless` in `Program.cs`. Direct methods on the builder enable specific login flows:

```csharp
using Huia.Entities;
using Huia.Headless;
using Huia.Headless.EntityFrameworkCore;

builder.Services
    .AddHuiaHeadless(huia =>
    {
        // 1. Set the issuer URI
        huia.UseIssuer("https://api.example.com");

        // 2. Enable email & password authentication
        huia.UseEmailAndPasswordLogin(password =>
        {
            password.RequireConfirmedEmail = false;
        });

        // 3. Optional: Enable passwordless SMS OTP login
        huia.UsePhoneLogin(phone =>
        {
            phone.DefaultCountry = "US";
            phone.OtpLifetime = TimeSpan.FromMinutes(5);
        });

        // 4. Optional: Enable external social providers
        huia.UseExternalLogin(external =>
        {
            external.AddGoogle("google-client-id", "google-client-secret");
            external.AllowReturnUrlPrefix("https://app.example.com/");
        });
    })
    .AddEntityFrameworkCoreStores<HuiaDbContext<HuiaUser, HuiaRole, string>>();
```

---

## 4. Map Endpoints in HTTP Pipeline

In single-tenant headless mode, standard ASP.NET Core authentication and authorization middleware are used directly:

```csharp
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Maps identity/register, identity/login, identity/me, phone, and passkey routes
app.MapHuiaHeadlessEndpoints();

// Optional: Map administrative endpoints under /admin/*
app.MapHuiaHeadlessAdminEndpointsGroup()
   .RequireAuthorization(policy => policy.RequireRole("Administrator"));

app.Run();
```

---

## 5. Endpoints Overview

`MapHuiaHeadlessEndpoints()` automatically exposes the following JSON endpoints:

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/identity/register` | `POST` | Anonymous | Register account (`email`, `password`, `firstName`, `lastName`) |
| `/identity/login` | `POST` | Anonymous | Sign in with email and password, returning bearer token |
| `/identity/refresh` | `POST` | Anonymous | Exchange a refresh token for fresh access/refresh tokens |
| `/identity/me` | `GET` | Bearer | Current user profile (`sub`, `email`, `firstName`, `lastName`, `roles`) |
| `/identity/phone/start` | `POST` | Anonymous | Request SMS OTP for a phone number |
| `/identity/phone/verify` | `POST` | Anonymous | Verify SMS OTP code |
| `/identity/phone/complete-profile` | `POST` | Anonymous | Complete first-time profile for phone signup |
| `/identity/account/external/{provider}` | `GET` | Anonymous | Initiate external social login challenge |
| `/identity/account/external/exchange` | `POST` | Anonymous | Exchange one-time external code for bearer tokens |

---

## 6. Frontend Clients

Rather than hand-crafting HTTP requests, first-party frontend packages integrate seamlessly with `Huia.Headless`:

- **Shared TypeScript**: [`huia-auth-core`](/core/overview) exports `HuiaHeadlessClient`.
- **Next.js**: [`next-huia-headless`](/next-headless/overview) provides server route handlers and client React hooks.
- **Nuxt.js**: [`nuxt-huia-headless`](/nuxt-headless/overview) provides Nuxt composables (`useHuia()`) and SSR session handling.
