# Huia.AspNetCore

The ASP.NET Core integration for the Huia identity provider.

```csharp
builder.Services.AddHuia(huia =>
{
    huia.UseIssuer("https://id.example.com");
    huia.AddTenant("acme", tenant => { /* ... */ });
});

var app = builder.Build();
app.UseHuia();          // localization -> multi-tenant -> routing -> authn -> authz
app.MapHuiaEndpoints(); // connect, manage, admin, account UI
```

`UseHuia()` fixes the middleware order so the Finbuckle base-path strategy runs before routing.
Cookie hardening is always on; call `AddHuiaSecurityHeaders()` to opt into the CSP / HSTS layer.
`MapHuiaEndpoints()` also maps `/health/live` and `/health/ready` (see the getting-started guide).

## Account-UI assets

The Razor Pages account UI is styled with [Basecoat](https://basecoatui.com) (Tailwind CSS +
`basecoat-css`), plus vendored `flag-icons` and FontAwesome brand marks and a `libphonenumber-js`
bundle for client-side phone validation. The built output under `wwwroot/` is committed — CI checks
it's up to date.

The `BuildAccountUiAssets` target in `Huia.AspNetCore.csproj` regenerates it as part of the build
(it runs the Tailwind CLI and copies the vendored assets out of `node_modules`, restoring them with
`npm ci` on first run). It needs `npm` on `PATH`; a build without Node keeps the committed output
untouched. After editing `Assets/styles/huia.css` or any `Areas/**/*.cshtml` / `Emails/**/*.cshtml`
markup that changes which utility classes are used, rebuild and commit the result:

```bash
dotnet build src/Huia.AspNetCore/Huia.AspNetCore.csproj -p:ForceAccountUiAssets=true
```

Pass `-p:SkipAccountUiAssets=true` to skip the target entirely.
