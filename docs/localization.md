# Localization

Huia's Identity UI pages and transactional emails are localized. English (`en`) and Arabic (`ar`,
right-to-left) ship out of the box.

## How it works

`AddHuia(...)` registers `AddLocalization()` and a `RequestLocalizationOptions` built from
`huia.Localization`; `app.UseHuia()` applies it (`UseRequestLocalization()`) — the culture is picked up from
a query string (`?culture=ar`), a cookie, or the `Accept-Language` header, using ASP.NET Core's standard
`RequestCultureProvider`s.

Every Razor page under `Areas/Identity/Pages` has `IStringLocalizer<HuiaResources>` injected automatically
(via `_ViewImports.cshtml`) as `Localizer`, resolving strings from `HuiaResources.resx` (English, the
neutral/fallback resource) and `HuiaResources.ar.resx` (Arabic). `HuiaEmailTemplate` and `SmtpEmailSender`
implementations should inject the same `IStringLocalizer<HuiaResources>` to localize transactional emails.

The layout sets `<html lang="..." dir="rtl|ltr">` from `CultureInfo.CurrentUICulture`, so Arabic renders
right-to-left automatically — both in the browser and in emails (`HuiaEmailTemplate` sets the same
attributes on the email's `<html>` tag).

## Adding a culture

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.Localization.AddCulture("fr");
        huia.Localization.SetDefaultCulture("fr"); // optional; defaults to "en"
    })
    .WithEntityFrameworkStores<AppDbContext>();
```

Then supply your own `HuiaResources.fr.resx` next to your project (or via a satellite resource assembly)
with the same keys as `HuiaResources.resx` — see
[src/Huia/Localization/HuiaResources.resx](../src/Huia/Localization/HuiaResources.resx) for the full key
list.

## Custom `ValidationAttribute`s

`AddDataAnnotationsLocalization`'s `DataAnnotationsModelValidatorProvider` re-localizes *built-in*
DataAnnotations attributes' (`[Required]`, `[EmailAddress]`, `[StringLength]`, ...) server-side error
message automatically, using their `ErrorMessage` as the lookup key — but not a *custom*
`ValidationAttribute` subclass's: its `data-val-*` client-side hint localizes correctly, but the rendered
server-side `ModelState` error stays English regardless of a matching resx entry (confirmed empirically,
not documented behavior). A custom attribute needs to resolve `IStringLocalizer<HuiaResources>` itself and
look its own `ErrorMessage` up explicitly — see
[PersonNameAttribute.cs](../src/Huia/Common/PersonNameAttribute.cs) (used by `FirstName`/`LastName` on the
Register/external-login/phone-login-confirmation pages) for the pattern: resolve the localizer via
`ValidationContext.GetService(typeof(IStringLocalizer<HuiaResources>))` inside `IsValid`, then index into it
with the same literal `ErrorMessage` string convention every other entry in `HuiaResources.resx` uses.

## Identity's validation messages

`HuiaUserManager`/`HuiaRoleManager`'s own validation messages (e.g. "Passwords must have at least one
digit") come from `HuiaErrorDescriber` (in the dependency-free `Huia.Identity` package), whose virtual
methods return hardcoded English defaults — the same split ASP.NET Core Identity itself uses between
`IdentityErrorDescriber` and its localization layer. The main `Huia` package registers
`Huia.Emails.LocalizedHuiaErrorDescriber`, a `HuiaErrorDescriber` subclass, as the default implementation
via `AddHuia(...)`; it resolves `IdentityError{MethodName}` keys (e.g. `IdentityErrorPasswordTooShort`)
from the same `HuiaResources.resx`/`HuiaResources.ar.resx` used elsewhere, so English/Arabic messages work
out of the box with no extra setup.

Register your own `HuiaErrorDescriber` after `AddHuia` if you want different wording:

```csharp
builder.Services.AddSingleton<HuiaErrorDescriber, MyLocalizedErrorDescriber>();
```

Subclass `LocalizedHuiaErrorDescriber` if you want to keep using `HuiaResources`-style localization (just
resolving different/additional resource keys), or subclass `HuiaErrorDescriber` directly if you want to
skip localization entirely and return your own hardcoded strings.
