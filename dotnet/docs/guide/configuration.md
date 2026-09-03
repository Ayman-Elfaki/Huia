# Configuration

The options tree is rooted at `HuiaOptions` and validated in one pass before anything is registered.

- `Issuer` / `PublicUrl` / `DisableTransportSecurityRequirement`
- `Email` / `Sms` — root delivery settings, merged per tenant
- `Keys` — the signing-key lifecycle (`RotationInterval`, `ActivationDelay`, `RetentionPeriod`, ...)
- `Tenants[id]` — `Authentication` (password + passwordless umbrella), `Lockout`, `Branding`, `Clients`

Every node exposes `Validate()`; failures throw `HuiaOptionsException` with a path-prefixed list.

## Sign-in methods

Both sign-in families are opt-in and symmetric — a tenant with neither fails validation:

```csharp
tenant.Authentication.UsePasswordFlow();                  // interactive username/password
tenant.Authentication.UsePasswordFlow(p =>                 // …with a stricter policy
    p.MinimumLength = 12);
tenant.Authentication.UsePasswordlessFlow(pwl => pwl.UsePhoneLogin());
```

`UsePasswordFlow()` sets `Authentication.Password.Enabled`; that flag is `false` until it is called.

### Password-flow policy

`Authentication.Password` also carries the per-tenant sign-in policy:

- `RequireConfirmedEmail` (default `true`) — an interactive password sign-in needs a confirmed email.
- `RequireUniqueEmail` (default `false`) — add Identity's tenant-scoped email-uniqueness check.
- `AllowSelfServiceRegistration` (default `true`) — anonymous visitors may create their own account.
  Call `tenant.DisableRegistration()` to turn it off (the account UI then hides the "create an
  account" link and the register page returns 404).

The non-interactive `password` grant (ROPC) is **not** supported — use the authorization-code or
device-authorization flow instead.

## Custom scopes

Beyond the standard OIDC scopes, a tenant can declare its own. They are seeded at start-up and
isolated per tenant (a client in one tenant cannot request a scope owned by another):

```csharp
tenant.AddScope("reports:read", scope =>
{
    scope.DisplayName = "Read reports";
    scope.Resources.Add("reports-api");   // audience granted by the scope
});
```

Scopes can also be managed at runtime through the admin API: `GET /admin/scopes?tenant=<id>`,
`POST /admin/scopes`, `PUT /admin/scopes/{name}`, `DELETE /admin/scopes/{name}?tenant=<id>`. The same
API has full CRUD for users (`/admin/users`) and clients (`/admin/clients`), and create + revoke for
signing keys (`/admin/keys`, `POST /admin/keys/{id}/revoke`). Anything created this way is stamped
`huia:origin = dynamic`; a code-seeded client or scope is read-only (a mutating call returns `409`).

## Account UI error pages

`UseHuia()` installs `UseStatusCodePagesWithReExecute`, so a bare `404` / `401` / `403` / `500` (for
example following a link into a disabled tenant, or clicking *Register* where
`DisableRegistration()` was called) renders a branded page that reuses the tenant's card layout and
localised strings. Requests under `/connect`, `/manage`, `/admin`, `/.well-known` and anything that
asked for `application/json` get the bare status code with no HTML, so API and protocol clients are
unaffected.

## Self-service profile (`/manage`)

The token-protected `/manage/*` API lets a signed-in user edit their own account. Which contact
details are editable depends on how the account signs in:

| Account type | Owns | `PUT /manage/email` | `PUT`/`DELETE /manage/phone` |
| --- | --- | --- | --- |
| Password (email + password) | email | allowed | **rejected** — a password account has no phone number |
| External (OIDC provider) | email | allowed | **rejected** |
| Phone (SMS one-time code) | phone number (also the username) | **rejected** | change allowed; **remove rejected** |

When a phone-login account changes its number, the username is moved with it
(`UserManager.SetUserNameAsync`), so the new number stays usable for sign-in.

## External login

An external sign-in goes through the OpenIddict client, then a single dispatcher
(`identity/account/externallogincallback`) that uses `SignInManager.GetExternalLoginInfoAsync`:

- **Already signed in** — the provider is linked to the current account. Users manage their linked
  providers at `identity/account/externallogins` (a Razor page) or through
  `GET` / `DELETE /manage/external-logins`; the last remaining sign-in method can't be removed.
- **Logged out, provider already linked** — signs straight in.
- **Logged out, email matches a local account** — linked only when the tenant opted in with
  `ext.LinkExistingAccountsByEmail()` **and** the local email is confirmed **and** the provider's
  `email_verified` isn't `false`. Otherwise the sign-in is refused with "email already registered" and
  no second account is created.
- **Otherwise** — a name is collected and a new account is created.

When a session was federated, `/connect/logout` also ends the upstream provider's session (RP-initiated
end-session via the OpenIddict client; the provider must register the tenant's
`{tenant}/signout-callback-oidc` as a post-logout redirect URI).
