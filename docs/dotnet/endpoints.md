# Endpoints

All routes live under the tenant base path, `/{tenant}/…`. `MapHuiaEndpoints()` maps the protocol,
account-UI, external-login and self-service groups; `MapHuiaAdminEndpoints()` maps the admin group
and returns the route-group builder so the host attaches the policy.

## Protocol (`/{tenant}/connect`)

| Route | Purpose |
|---|---|
| `GET .well-known/openid-configuration` | per-tenant discovery — issuer `{Issuer}/{tenant}`, advertises `pushed_authorization_request_endpoint` |
| `GET .well-known/jwks` | the tenant's **published** signing keys (`HuiaTenantJwksHandler`) |
| `GET connect/authorize` | Authorization Code + PKCE (+ PAR); redirects to the account UI when not signed in |
| `POST connect/par` | RFC 9126 Pushed Authorization Request → `{ request_uri, expires_in }` |
| `POST connect/token` | code / refresh / client-credentials grants — **`application/x-www-form-urlencoded` only** |
| `GET connect/userinfo` | the standard claims for the bearer token |
| `GET connect/end_session` | RP-initiated logout (`post_logout_redirect_uri`, `id_token_hint`) |
| `POST connect/device` · `GET connect/verification` | device authorization grant |

Tokens are minted with the **tenant's rotated RSA key** and the per-tenant issuer; a token issued
for tenant A is `401 invalid_token` at tenant B.

## Account UI (`/{tenant}/identity/account`)

Razor Pages (`Areas/Identity/Pages/Account`), English + Arabic (RTL), one committed JS bundle.

| Page | Notes |
|---|---|
| `login` | one page, tabs for password + phone when both are enabled; a "sign in with a passkey" button and external-provider buttons below |
| `loginwith2fa` | passkey (or recovery-code) step-up after a password sign-in for an account that requires a second factor |
| `passkeys` | cookie-authenticated passkey management (list / add / rename / delete, the second-factor toggle) — the browser-user counterpart of `manage/passkeys/*` |
| `register` | 404 when `AllowSelfServiceRegistration` is off; auto-signs-in when the tenant does not `RequireConfirmedEmail`, else sends a confirmation link |
| `verifyotp` | step 2 of the phone flow — segmented code input sized from `CodeLength`, resend cooldown |
| `completeprofile` | collects a name for a phone auto-provisioning signup or a first-time external sign-in — the account is created **here**, never with blank names earlier |
| `forgotpassword` / `resetpassword` / `confirmemail` | email flows; carry `returnUrl` through |
| `externallogins` | link / unlink providers for a signed-in user |
| `status/{code}` | the `UseStatusCodePagesWithReExecute` target — styled for HTML, a bare code for `/connect`, `/manage`, `/admin`, `/.well-known` and JSON |

## Self-service (`/{tenant}/manage`)

Bearer-authenticated (`HuiaConstants.Policies.Api`, which pins the OpenIddict-validation scheme); the
caller is resolved from the token `sub`.

| Route | Guard (by `HuiaUserType`) |
|---|---|
| `GET/PUT manage/profile`, `PUT manage/password` | — |
| `GET/PUT manage/email` + `POST manage/email/confirm` | a `Phone` account cannot set an email |
| `GET/PUT manage/phone` + `POST manage/phone/confirm` + `DELETE manage/phone` | a `Password` / `External` account cannot set a phone; a `Phone` account cannot remove it (a phone-user's username tracks the number in lock-step) |
| `GET manage/external-logins` + `DELETE manage/external-logins/{provider}/{key}` | an unlink is refused when it would leave no other sign-in method |
| `POST manage/passkeys/creation-options` + `POST/GET/PATCH/DELETE manage/passkeys[/{id}]` | passkey registration and management; `DELETE` returns `409` for the last passkey while the second factor is on |
| `GET/PUT manage/passkeys/two-factor` + `POST manage/passkeys/recovery-codes` | enable / disable the passkey second factor; enabling returns ten recovery codes once |

Passkey ceremonies also have anonymous routes under `identity/account/passkey/*`
(`assertion-options`, `assertion`, `2fa-options`, `2fa`) — antiforgery- and origin-checked, mapped
with the account UI. See [Passkeys](/dotnet/passkeys).

## Admin (`/master/admin` in the sample)

`MapHuiaAdminEndpoints()` is mapped without a policy; the host attaches one:

```csharp
app.MapHuiaAdminEndpoints()
   .RequireAuthorization(p => p.RequireTenants("master").RequireRole(HuiaConstants.Roles.Administrator));
```

| Route | |
|---|---|
| `GET admin/tenants` | read-only — id, display name, sign-in methods, client count |
| `admin/users` `GET/POST/PUT/DELETE` (+ `…/{id}/roles`) | create a password **or** phone account; keyset paged; tenant filter |
| `admin/roles` `GET/POST/PUT/DELETE` | per-tenant; a role with members cannot be deleted |
| `admin/clients` `GET/POST/PUT/DELETE` | dynamic clients only — `409` on a static one; `HuiaClientDescriptor.Validate()` runs before the mapper |
| `admin/scopes` `GET/POST/PUT/DELETE` | dynamic scopes only |
| `admin/keys` `GET/POST` + `POST admin/keys/{id}/revoke` + `DELETE` | mint pending/active (active demotes the current active to *rotated*); revoke → *retired* (`409` if it is the only active key); delete only a *retired* key |

Lists use keyset pagination (`after` / `before` / `size`); `KeysetQueryModel` is built from the query
string by hand (`[AsParameters]` 400s on minimal APIs).

## Pipeline

`UseHuia()` fixes the order: `UseExceptionHandler` → `UseStatusCodePagesWithReExecute` →
`UseRequestLocalization` → **`UseMultiTenant`** (before routing — the base-path strategy rebases
`PathBase` to `/{tenant}`) → `HuiaSecurityHeadersMiddleware` (opt-in) → `UseStaticFiles` →
`UseRouting` → `UseAuthentication` → `UseAuthorization`.
