# Huia .NET Configuration & Client Recipes Reference

Recipes for configuring tenants, client applications, authentication methods, and security settings in `Huia.OpenId`.

---

## 1. Tenant Authentication Recipes

### A. Password Policy & Lockout
```csharp
tenant.Authentication.UseEmailAndPasswordLogin(password =>
{
    password.MinimumLength = 12;
    password.RequireNonAlphanumeric = true;
    password.RequireDigit = true;
    password.RequireUppercase = true;
    password.RequireConfirmedEmail = true; // Requires email verification before login
    password.MaxFailedAccessAttempts = 5;
    password.LockoutDuration = TimeSpan.FromMinutes(15);
});
```

### B. Passkeys (FIDO2 / WebAuthn)
Enables discoverable passkey login and conditional UI autofill:
```csharp
tenant.Authentication.UsePasskeyLogin(passkey =>
{
    passkey.UserVerification = PasskeyUserVerification.Preferred; // Discouraged, Preferred, or Required
    passkey.RequireResidentKey = true;
});
```

### C. Passwordless Phone SMS
Configures SMS one-time codes with anti-abuse rate limits:
```csharp
tenant.Authentication.DefaultPhoneCountry = "US"; // ISO 3166-1 alpha-2
tenant.Authentication.UsePhoneLogin(phone =>
{
    phone.DefaultCountry = "US";
    phone.AllowAutoProvisioning = true; // Creates a user if phone number doesn't exist
    phone.SuccessfulLoginsPerWindow = 3;
    phone.SuccessfulLoginWindow = TimeSpan.FromMinutes(10);
    phone.SuccessfulLoginsPerDay = 10;
    phone.MaxFailedAccessAttempts = 5;
});
```

### D. External Identity Federation
Supports Google, GitHub, and generic OpenID Connect upstream providers:
```csharp
tenant.Authentication.UseExternalLogin(ext =>
{
    // Google Sign-In
    ext.AddGoogle("your-google-client-id", "your-google-client-secret", g =>
    {
        g.Scopes.Add("profile");
        g.Scopes.Add("email");
    });

    // Custom upstream OIDC Provider (e.g., Azure AD, Okta, Auth0)
    ext.AddOpenIdConnect(
        scheme: "corporate-idp",
        clientId: "corp-client-id",
        clientSecret: "corp-client-secret",
        authority: "https://login.microsoftonline.com/{tenant-id}/v2.0",
        configure: oidc =>
        {
            oidc.DisplayName = "Corporate SSO";
            oidc.Scopes.Add("openid");
            oidc.Scopes.Add("profile");
            oidc.Scopes.Add("email");
        });

    // Automatically link external logins to existing local accounts with confirmed matching email
    ext.EnableAccountsLinking();
});
```

---

## 2. Client Application Registration Types

### A. Confidential Server-Side Web Application (Next.js / Nuxt / ASP.NET MVC)
Uses Authorization Code Flow + PKCE with client authentication (client secret):
```csharp
tenant.AddServerSideWebApplication("my-web-app", "strong-client-secret", client =>
{
    client.DisplayName = "Customer Portal";
    client.RedirectUris.Add(new Uri("https://app.example.com/api/auth/callback"));
    client.PostLogoutRedirectUris.Add(new Uri("https://app.example.com/"));
    client.HomeUris.Add(new Uri("https://app.example.com/"));

    // Enforce Pushed Authorization Requests (RFC 9126) for highest security
    client.RequirePushedAuthorizationRequests();

    // Token Lifetimes
    client.Token.AccessToken = TimeSpan.FromMinutes(15);
    client.Token.RefreshToken = TimeSpan.FromDays(30);
});
```

### B. Public Single Page Application (SPA / Mobile)
Uses Authorization Code Flow + PKCE without client secret:
```csharp
tenant.AddSinglePageApplication("my-spa-client", client =>
{
    client.DisplayName = "Mobile / SPA App";
    client.RedirectUris.Add(new Uri("https://spa.example.com/callback"));
    client.PostLogoutRedirectUris.Add(new Uri("https://spa.example.com/"));
});
```

### C. Machine-to-Machine (Worker Services / Cron Jobs)
Uses Client Credentials Grant:
```csharp
tenant.AddMachineToMachineApplication("billing-worker", "worker-api-secret");
```

---

## 3. Scopes and Roles Definition

```csharp
// Define static scopes
tenant.AddScope("orders:read", scope =>
{
    scope.DisplayName = "Read Orders";
    scope.Description = "Permission to read order details.";
    scope.Resources.Add("orders-api");
});

// Seed static default roles
tenant.AddRoles("customer", "manager", "support");
```
