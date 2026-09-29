# Security Headers

Huia provides opt-in, pre-configured HTTP response security headers designed to protect both the Razor account UI and API surfaces against Cross-Site Scripting (XSS), clickjacking, MIME sniffing, and referrer leaks.

## Enabling Security Headers

To enable security headers, add the middleware to your ASP.NET Core service registration pipeline:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register Huia with security headers
builder.Services.AddHuia(options => { ... })
    .AddHuiaSecurityHeaders(options =>
    {
        // Custom header configuration (optional)
        options.AppendNonceToInlineScripts = true;
    });

var app = builder.Build();

app.UseHuia();

app.Run();
```

The middleware is inserted right after tenant resolution, ensuring all downstream responses (including identity endpoints and error status pages) receive hardened headers.

---

## Default Directives

When enabled, the following HTTP headers are emitted on every response:

### Content-Security-Policy (CSP)
A per-request cryptographic nonce is generated and injected into the CSP header and request items:

```http
Content-Security-Policy: default-src 'self'; script-src 'self' 'nonce-{RANDOM_NONCE}'; style-src 'self' 'nonce-{RANDOM_NONCE}'; object-src 'none'; base-uri 'self'; form-action 'self' https://accounts.google.com https://login.microsoftonline.com; frame-ancestors 'none';
```

- **Per-Request Nonce**: Injected automatically into the Razor account UI views.
- **Form Action Protection**: Restricts form submissions to `'self'` and registered external identity provider authorization endpoints.
- **Clickjacking Protection**: `frame-ancestors 'none'` blocks embedding in iframes.

### Strict-Transport-Security (HSTS)
When the request is delivered over HTTPS (and `DisableTransportSecurityRequirement` is not set):

```http
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

### X-Content-Type-Options
Prevents MIME-type sniffing:

```http
X-Content-Type-Options: nosniff
```

### Referrer-Policy
Reduces privacy leaks in outgoing referrers:

```http
Referrer-Policy: strict-origin-when-cross-origin
```

### Permissions-Policy
Disables unused browser capabilities:

```http
Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()
```

---

## Configuration Knobs

The `HuiaSecurityHeadersOptions` class exposes the following knobs:

| Option | Type | Default | Description |
|---|---|---|---|
| `AppendNonceToInlineScripts` | `bool` | `true` | Appends `'nonce-{random}'` to the `script-src` directive |
| `DisableScripts` | `bool` | `false` | Disables script execution entirely for pure API / headless hosts |
| `AdditionalScriptSrc` | `string[]` | `[]` | Additional trusted origins for external scripts (e.g. analytics or CDNs) |
| `AdditionalStyleSrc` | `string[]` | `[]` | Additional trusted origins for external stylesheets or web fonts |
| `AdditionalFormActionOrigins` | `string[]` | `[]` | Extra origins for external authentication redirects |

### Example: Adding Custom Script and Style Origins

```csharp
builder.Services.AddHuiaSecurityHeaders(options =>
{
    options.AdditionalScriptSrc = new[] { "https://cdn.jsdelivr.net" };
    options.AdditionalStyleSrc = new[] { "https://fonts.googleapis.com" };
});
```
