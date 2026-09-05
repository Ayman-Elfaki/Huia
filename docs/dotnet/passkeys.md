# Passkeys (WebAuthn / FIDO2)

A tenant enables passkeys with `UsePasskeyLogin()`, called on `tenant.Authentication`. It gives that
tenant:

- **passkey-first sign-in** — a "Sign in with a passkey" button that leads the login page and runs a
  usernameless (discoverable) WebAuthn assertion, plus conditional-UI autofill on the email field;
- a **prompt after sign-up** — the first time an account is created and signed in, a one-time
  interstitial offers to set up a passkey (skippable).

Every credential Huia issues is a **discoverable resident key** (`residentKey: "required"`) so a
usernameless assertion can find it.

Passkey storage rides on ASP.NET Core Identity 10's built-in support (`IdentityUserPasskey`, the EF
`IUserPasskeyStore`, `IdentityPasskeyOptions`, the `SignInManager` / `UserManager` passkey methods).
`HuiaDbContext` pins the Identity schema to `Version3` so the `HuiaUserPasskeys` table is always in
the model, and — because the context derives from Finbuckle's — a credential row carries a
`TenantId`, is filtered by the ambient tenant on read, and is stamped on write like every other
Identity row.

## Configuration

The relying party is **per tenant** — there is no host-wide passkey configuration.

```csharp
tenant.Authentication.UsePasskeyLogin(passkey =>
{
    passkey.RelyingPartyId = "acme.example.com";  // null (default) → the request host
    passkey.AllowedOrigins.Add("https://app.acme.example.com");  // extra origins beyond the request origin
    passkey.UserVerification = PasskeyUserVerification.Required;        // Preferred | Required | Discouraged
    passkey.AuthenticatorAttachment = PasskeyAuthenticatorAttachment.Any;  // Any | Platform | CrossPlatform
    passkey.AuthenticatorTimeout = TimeSpan.FromMinutes(2);   // 30s – 10m
});
```

`RelyingPartyId` left unset uses `Request.Host.Host`, and origin validation is the framework's
same-origin check.

::: warning Isolating credentials per tenant
A browser only accepts an RP id that is a registrable-domain suffix of the host serving the page.
Distinct per-tenant RP ids therefore isolate credentials **only if each tenant is fronted by its own
host** (for example `acme.example.com`, `globex.example.com`) at your reverse proxy — Huia validates
the RP id format, not that it matches the host. On a single shared host every tenant's effective RP
id is that host; a discoverable account picker can then surface another tenant's credential, the
assertion is rejected (the credential row is tenant-filtered), and the login page silently falls back
to the password / phone form.
:::

## Endpoints

Anonymous (mapped with the account UI, `404` unless the tenant enabled passkeys):

| Route | Purpose |
| --- | --- |
| `POST identity/account/passkey/assertion-options` | Start a discoverable assertion. |
| `POST identity/account/passkey/assertion` | Finish it and sign in (`amr: passkey`). |

Both validate antiforgery (`X-Huia-CSRF` header) and the request `Origin`.

Token-protected, on the `manage` group (`Huia:Api` policy, user from `sub`):

| Route | Purpose |
| --- | --- |
| `POST manage/passkeys/creation-options` | WebAuthn creation options for the caller. |
| `POST manage/passkeys` | Register the attested credential (`{ credential, name }`). |
| `GET manage/passkeys` | List the caller's credentials. |
| `PATCH manage/passkeys/{id}` | Rename one. |
| `DELETE manage/passkeys/{id}` | Remove one — `409` if it is the account's only sign-in method. |

Browser users who signed in directly at the identity provider manage their credentials on the
cookie-authenticated `/{tenant}/identity/account/passkeys` page instead.

## Sign-up prompt

After a first sign-in that created the account (email/password registration, phone one-time-code
first sign-in, profile completion) — and, once, on the first password sign-in that follows email
confirmation — the user is sent to `/{tenant}/identity/account/passkeyenroll`. It offers **Set up a
passkey** (one tap) or **Skip for now**; both continue to the original return URL. When WebAuthn is
unavailable the page skips itself. It is shown at most once per account (an `AspNetUserTokens` entry
under the `Huia.Passkey` provider records it); registering a passkey any other way also clears it.

## Login page & graceful fallback

WebAuthn is feature-detected **before first paint** — the layout's synchronous head script sets
`data-webauthn` on `<html>`, and `huia.css` hides the passkey affordances when it is absent. No
flash, no layout shift.

- **no `window.PublicKeyCredential`** → the passkey control is never rendered; the Email/Password +
  Phone forms are the page.
- **supported** → a slim outlined "Sign in with a passkey" button leads (the fast path); the email
  field carries `autocomplete="username webauthn"`, so most returning users pick their passkey from
  the browser's own autofill dropdown and never click anything.
- **cancelled / sensor failure / `NotAllowedError` / `NotSupportedError` / timeout / an unknown
  credential for this origin** → the button resets and focus moves to the visible identifier field.
  No banner, no message.

## Managing credentials

`/{tenant}/identity/account/passkeys` (cookie session) and the bearer `manage/passkeys/*` API expose
the same operations. In the UI:

- **Add** runs the ceremony and stores the credential with a name guessed from the device
  (`This device` / `Phone` / `Security key`); every row's name is then **click-to-edit**.
- A credential the authenticator reports as synced shows a **Synced** badge (`IdentityUserPasskey`'s
  `IsBackedUp`).
- **Remove** is a two-step inline confirm and is blocked when the passkey is the account's only
  sign-in method (`409` on the API).

The discoverable assertion ceremony keeps its challenge in the short-lived
`huia.2fa-user.{tenant}` cookie (ASP.NET Core Identity's two-factor-user scheme, which the framework's
passkey helpers reuse for ceremony state).

## Events

| Event | When |
| --- | --- |
| `PasskeyRegisteredEvent` | A credential was registered. |
| `PasskeyRemovedEvent` | A credential was removed. |
| `UserLoggedInEvent` with `Method = "passkey"` | Discoverable passkey sign-in. |

Registering a passkey through the sign-up interstitial raises `PasskeyRegisteredEvent` like any other
registration path.

## Upgrading

`HuiaDbContext` now includes the `HuiaUserPasskeys` table (Identity schema `Version3`). The sample
host and the tests create their schema with `EnsureCreated`, so they pick it up automatically; a
deployment that keeps EF Core migrations must generate one to add the table (and the `Version2`
base-table index adjustments that `Version3` implies).
