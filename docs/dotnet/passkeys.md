# Passkeys (WebAuthn / FIDO2)

A tenant enables passkey sign-in with `UsePasskeyLogin()`, called on `tenant.Authentication`. It
adds two things:

- a **discoverable, one-tap** sign-in on the account UI — a "Sign in with a passkey" button that
  runs a usernameless WebAuthn assertion, plus conditional-UI autofill on the email field; and
- the option for a user to **require a passkey as a second factor** after their password.

Passkey storage rides on ASP.NET Core Identity 10's built-in support (`IdentityUserPasskey`, the EF
`IUserPasskeyStore`, `IdentityPasskeyOptions`, the `SignInManager` / `UserManager` passkey methods).
`HuiaDbContext` pins the Identity schema to `Version3` so the `HuiaUserPasskeys` table is always in
the model, and — because the context derives from Finbuckle's — a credential row carries a
`TenantId`, is filtered by the ambient tenant on read, and is stamped on write like every other
Identity row.

## Configuration

```csharp
// Host-wide relying-party identity (shared by every tenant — Huia serves them all from one origin).
huia.ConfigurePasskeys(passkey =>
{
    passkey.RelyingPartyId = null;                 // null → the request host (correct for one host)
    passkey.RelyingPartyName = "Example";          // null → the tenant display name, else "Huia"
    passkey.AllowedOrigins.Add("https://app.example.com");   // extra origins beyond the request origin
});

// Per tenant.
tenant.Authentication.UsePasskeyLogin(passkey =>
{
    passkey.UserVerification = PasskeyUserVerification.Required;        // Preferred | Required | Discouraged
    passkey.AuthenticatorAttachment = PasskeyAuthenticatorAttachment.Any;  // Any | Platform | CrossPlatform
    passkey.AllowSecondFactor = true;             // may a user require a passkey as a 2nd factor?
    passkey.AuthenticatorTimeout = TimeSpan.FromMinutes(2);   // 30s – 10m
});
```

Because there is a single relying party for all tenants, credentials are isolated **by the database
query filter**, not by a distinct relying-party id. A credential registered under tenant `a` is
invisible — and unusable — under tenant `b`.

Behind a reverse proxy, or when several hostnames should share credentials, set `RelyingPartyId`
explicitly and list the browser origins in `AllowedOrigins`.

## Endpoints

Anonymous (mapped with the account UI, `404` unless the tenant enabled passkeys):

| Route | Purpose |
| --- | --- |
| `POST identity/account/passkey/assertion-options` | Start a discoverable assertion. |
| `POST identity/account/passkey/assertion` | Finish it and sign in (`amr: passkey`). |
| `POST identity/account/passkey/2fa-options` | Start the step-up assertion for the pending password user. |
| `POST identity/account/passkey/2fa` | Finish the step-up (`amr: pwd, passkey, mfa`). |

All four validate antiforgery (`X-Huia-CSRF` header) and the request `Origin`.

Token-protected, on the `manage` group (`Huia:Api` policy, user from `sub`):

| Route | Purpose |
| --- | --- |
| `POST manage/passkeys/creation-options` | WebAuthn creation options for the caller. |
| `POST manage/passkeys` | Register the attested credential (`{ credential, name }`). |
| `GET manage/passkeys` | List the caller's credentials. |
| `PATCH manage/passkeys/{id}` | Rename one. |
| `DELETE manage/passkeys/{id}` | Remove one — `409` if it is the last while the second factor is on. |
| `GET manage/passkeys/two-factor` | `{ enabled, hasPasskey, recoveryCodesLeft }`. |
| `PUT manage/passkeys/two-factor` | Enable / disable; enabling needs ≥1 passkey and returns 10 recovery codes once. |
| `POST manage/passkeys/recovery-codes` | Regenerate the recovery codes (returned once). |

Browser users who signed in directly at the identity provider manage their credentials on the
cookie-authenticated `/{tenant}/identity/account/passkeys` page instead.

## Second factor

Turning on the second factor (`PUT manage/passkeys/two-factor { enabled: true }`, or the toggle on
the Passkeys page) sets `TwoFactorEnabled` on the account and issues ten recovery codes. A later
password sign-in then redirects to `/{tenant}/identity/account/loginwith2fa`, which offers a passkey
assertion or a recovery code. "Don't ask again on this device" remembers the browser
(`huia.2fa.{tenant}` cookie) and skips the step next time.

The pending-second-factor user and the passkey ceremony challenge both live in the short-lived
`huia.2fa-user.{tenant}` cookie; the account id is also carried in the Data-Protection-wrapped
`flow` token so the step-up survives the ceremony overwriting that cookie.

## Events

| Event | When |
| --- | --- |
| `PasskeyRegisteredEvent` | A credential was registered. |
| `PasskeyRemovedEvent` | A credential was removed. |
| `UserLoggedInEvent` with `Method = "passkey"` | Discoverable passkey sign-in. |
| `UserLoggedInEvent` with `Method = "mfa"` | Password followed by a passkey (or recovery-code) step-up. |

## Upgrading

`HuiaDbContext` now includes the `HuiaUserPasskeys` table (Identity schema `Version3`). The sample
host and the tests create their schema with `EnsureCreated`, so they pick it up automatically; a
deployment that keeps EF Core migrations must generate one to add the table (and the `Version2`
base-table index adjustments that `Version3` implies).
