# Passwordless phone sign-in

Huia can let a user sign in with just a phone number and a one-time code (OTP) sent by SMS — no password.
This builds on ASP.NET Core Identity's own two-factor machinery (`UserManager<HuiaUser>.GenerateTwoFactorTokenAsync`/
`VerifyTwoFactorTokenAsync` with the built-in `"Phone"` token provider), not a bespoke code generator.

## Enabling it

```csharp
builder.Services.AddHuia(issuer, huia =>
    {
        huia.Authentication.UsePasswordlessFlow();
    })
    .WithEntityFrameworkStores<AppDbContext>();
```

Register your own `ISmsSender<HuiaUser>` before calling `AddHuia` to actually deliver codes — the default,
`NoOpSmsSender`, just logs the code (with the phone number masked) when `IHostEnvironment.IsDevelopment()` is
true, and logs nothing otherwise:

```csharp
builder.Services.AddSingleton<ISmsSender<HuiaUser>, TwilioSmsSender>();
```

See `samples/Huia.TodoApi/Sms/TwilioSmsSender.cs` for a real (Twilio) implementation to copy from.

`UsePasswordlessFlow` also requires an `IHuiaPhoneNumberStore` to be registered —
`.WithEntityFrameworkStores<TContext>()` provides one automatically; a fully custom store implements it
directly (see [custom-store.md](custom-store.md)).

## Sign-in flow

1. **`/identity/account/login`** (or the phone tab of it, if `UseEmailAndPasswordFlow()` is also enabled —
   see [Hybrid auth UI](#hybrid-auth-ui) below) posts the phone number to `PhoneLoginModel`.
2. The number is normalized to E.164 (via `libphonenumber-csharp`) and rate-limited (see
   [Rate limiting](#rate-limiting) below). A code is generated and sent, and the pending state (which phone
   number, which account) is carried in a short-lived, `HttpOnly`, encrypted+signed `Huia.PhoneVerification`
   cookie — not TempData, and not a value the client can see or tamper with. The user is redirected to
   `PhoneLoginVerify`.
3. **`PhoneLoginVerify`** reads the pending phone number and account from the cookie (never from a posted or
   route value), and verifies the submitted code via `UserManager.VerifyTwoFactorTokenAsync(user, "Phone",
   code)`. A wrong code counts against the account's normal lockout (`UserManager.AccessFailedAsync`); too
   many wrong attempts routes to the same `Lockout` page a failed password sign-in does.
4. A **brand-new** account (this is its first-ever successful verification) is routed to
   **`PhoneLoginConfirmation`** to collect a first/last name before completing sign-in — mirroring
   `ExternalLoginConfirmation`'s explicit-consent step for a first-time external sign-in. A **returning**
   passwordless user signs in immediately.

## Changing your phone number

A signed-in user can change their own phone number through the Manage JSON API
(`/api/identity/manage/info/phone`) — gated behind `UsePasswordlessFlow()` being enabled, since it reuses that
flow's SMS-sending and rate-limiting infrastructure (`IPhoneOtpRateLimiter`/`ISmsSender<HuiaUser>` are only
registered in DI when the flow is on; both endpoints reject with a validation problem otherwise).

Unlike the sign-in flow above, this uses `UserManager<HuiaUser>.GenerateChangePhoneNumberTokenAsync`/
`ChangePhoneNumberAsync` — still the same `"Phone"` token provider under the hood, so SMS delivery works
identically, but the token's purpose string is bound to the specific new number (`"ChangePhoneNumber:" +
phoneNumber`), not just to the user. That means no pending-state cookie is needed the way `PhoneLoginVerify`
needs one for an anonymous caller: every Manage endpoint is already authenticated, so the client just resends
the same number it's verifying alongside the code.

1. **`POST /api/identity/manage/info/phone`** `{ newPhoneNumber }` — normalizes the number (must already be
   fully E.164-qualified, e.g. `+15551234567`; there's no separate country-picker field the way the sign-in
   form has one), rate-limits it via the same `IPhoneOtpRateLimiter`, generates and sends a code, and returns
   the number masked (`{ maskedPhoneNumber }`) for the client to display.
2. **`POST /api/identity/manage/info/phone/verify`** `{ newPhoneNumber, code }` — verifies via
   `ChangePhoneNumberAsync`, which sets `PhoneNumber`/`PhoneNumberConfirmed = true` in one call on success. A
   wrong code counts against the account's normal lockout (`AccessFailedAsync`/`IsLockedOutAsync`), exactly
   like a wrong sign-in OTP does.
3. **`DELETE /api/identity/manage/info/phone`** clears the number — no OTP needed, since removing data is
   lower-risk than adding/changing it (the same reasoning already applied to unlinking an external login). If
   the account is a `PhoneUser`, this is rejected instead: phone-based sign-in is unconditionally its only
   credential (a `PhoneUser` never has a password or an external login by construction), so clearing it would
   always strand the account.

**This flow can never turn a `StandardUser` into a phone-authenticated account.** Recording or confirming a
phone number through self-service account management must not silently grant a new sign-in path to an account
that didn't opt into passwordless sign-in through the dedicated registration flow — the same account-linking
boundary described under [Hybrid-auth security considerations](#hybrid-auth-security-considerations) above
applies here too. Only `PhoneUser`'s own type carries that meaning; a `StandardUser`'s phone number is purely
account-management data.

## Configuring the flow

Passwordless-specific settings — rate limits, the country picker's default, IP rate limiting, Turnstile —
live on one `PasswordlessFlowOptions` object, configured via a single callback:

```csharp
huia.Authentication.UsePasswordlessFlow(passwordless =>
{
    passwordless.DefaultCountryCode = "US"; // the phone form's country picker preselects this
});
```

ASP.NET Core Identity's own shared `IdentityOptions` (lockout, password policy, etc.) isn't configured
per-flow — it's inherently one shared configuration space regardless of which sign-in method(s) are enabled,
so it lives directly on `HuiaOptions` instead:

```csharp
huia.Identity = identity =>
{
    identity.Lockout.MaxFailedAccessAttempts = 5;
};
```

Runs directly against the same `IdentityOptions` instance ASP.NET Core Identity itself builds.

## Rate limiting

### Per phone number

Always enforced, checked before any user lookup/creation — so it throttles a number with no account at all
just as well as a registered one:

```csharp
huia.Authentication.UsePasswordlessFlow(passwordless =>
{
    passwordless.RateLimit.RequestsPerMinute = 1; // default
    passwordless.RateLimit.RequestsPerHour = 3;   // default
    passwordless.RateLimit.RequestsPerDay = 10;   // default
});
```

The default `IPhoneOtpRateLimiter` is in-memory (per instance); register your own implementation (e.g.
Redis-backed) before calling `AddHuia` if limits need to be shared across a scaled-out deployment.

### Per client IP address

Off by default — opt in with `EnableIpRateLimiting()`. This is a coarser, additional layer that catches a
script enumerating many different phone numbers from one source (each individual number never trips its own
limit, so the per-phone-number check alone can't):

```csharp
huia.Authentication.UsePasswordlessFlow(passwordless =>
{
    passwordless.EnableIpRateLimiting(ip =>
    {
        ip.RequestsPerMinute = 5;  // default
        ip.RequestsPerHour = 20;   // default
        ip.RequestsPerDay = 50;    // default
    });
});
```

The defaults are deliberately looser than the per-phone-number ones — a shared/NATed IP (corporate network,
mobile carrier CGNAT, VPN exit node) can plausibly represent many genuine users behind one address, so tune
this to the app's own expected traffic shape. `HttpContext.Connection.RemoteIpAddress` is what's partitioned
on; behind a reverse proxy, configure `UseForwardedHeaders()` yourself so this reflects the real client IP
rather than the proxy's. Like the per-phone-number limiter, register your own `IPhoneIpRateLimiter` before
`AddHuia` for a shared, scaled-out deployment.

## Bot protection (Cloudflare Turnstile)

Off by default — opt in with `UseTurnstile(siteKey, secretKey)` (get a pair from the Cloudflare dashboard,
dash.cloudflare.com → Turnstile). An additional, configurable layer against automated SMS-bombing scripts, on
top of both rate limits above:

```csharp
huia.Authentication.UsePasswordlessFlow(passwordless =>
{
    passwordless.UseTurnstile(
        siteKey: builder.Configuration["Turnstile:SiteKey"]!,
        secretKey: builder.Configuration["Turnstile:SecretKey"]!);
});
```

When configured, the phone sign-in form renders Cloudflare's widget (loaded live from
`challenges.cloudflare.com` — unlike every other script Huia.UI ships, this one can't be vendored, since
Cloudflare's own terms require serving it fresh rather than from a static copy) and `PhoneLoginModel`
verifies the resulting token against Cloudflare's `siteverify` endpoint before sending a code. Register your
own `ITurnstileVerifier` before `AddHuia` to swap in a different provider (e.g. hCaptcha, reCAPTCHA) instead.

## PII protection

Every log statement in the passwordless flow masks the phone number (e.g. `+15551234567` becomes
`+1***-***-4567`, via the internal `PhoneNumberMasker`) — the raw number is never written to logs.

## Anti-forgery

`PhoneLogin`, `PhoneLoginVerify`, and `PhoneLoginConfirmation` are plain Razor Pages — the framework's
automatic per-POST antiforgery check (the same one that protects `Login`/`Register`) applies unmodified, with
no extra code needed.

## Hybrid auth UI

With both `UsePasswordlessFlow()` and `UseEmailAndPasswordFlow()` enabled, `/identity/account/login` renders
them as a [Basecoat UI](https://basecoatui.com/) tab each — "Email" and "Phone" — rather than picking one.
External-provider buttons (if `UseExternalAuthenticationFlow(...)` is also enabled) render below the tabs
either way. With only one of the two enabled, its form renders directly with no tabs.

## Hybrid-auth security considerations

Running both email+password and passwordless phone sign-in on the same app introduces a few decisions worth
being explicit about:

1. **Being a `PhoneUser` (rather than a `StandardUser`) is the account-linking boundary.** A `PhoneNumber`
   present on a `StandardUser` for any other reason (a future SMS-2FA feature, an admin-entered contact
   number) never implicitly becomes a standalone sign-in path — only a `PhoneUser`, created through the
   passwordless flow (accounts don't change type after creation in this version), is reachable via phone+OTP
   alone. This is deliberate:
   password knowledge and OTP-over-SMS possession are not equivalent-strength proofs. SMS is interceptable via
   SIM-swap, SS7 exploits, or a compromised carrier account — none of which require compromising the account
   holder's actual credentials. Treating "has this phone number on file" as sufficient proof to sign into an
   *existing password account* would silently add a weaker authentication path to any account that ever had a
   phone number recorded for an unrelated reason. Contrast this with [external-provider password
   linking](external-providers.md#password-confirmed-linking-on-email-collision): there, proving password
   ownership genuinely *is* an equivalent-strength proof, which is why that auto-link is safe and this one
   isn't.
2. **A phone-number collision creates a second, distinct account, never a silent merge.** If a submitted
   number matches an existing account that isn't a `PhoneUser`, `PhoneLoginModel` creates a new `PhoneUser`
   row for the passwordless identity — the same phone number then exists on two separate rows.
   The alternative (silently reusing the existing account) would grant OTP-only access to whatever that
   account is actually protected by; the other alternative (an explicit "an account already exists" error)
   would leak account existence to anyone who submits the phone-entry form, with no proof of ownership
   required to even attempt it — a materially weaker bar than the external-login collision case, whose
   equivalent message only appears after a real OAuth handshake completed with the provider. This is a known,
   accepted UX edge case (a user who set a phone number for 2FA years ago and later tries passwordless with
   the same number ends up with two accounts), not a security gap.
3. **Layered brute-force / SIM-swap mitigation**: per-phone-number rate limiting on *requesting* codes (works
   even before an account exists) + per-account lockout on *verifying* codes (via the same
   `UserManager.AccessFailedAsync`/`IsLockedOutAsync` counters a password sign-in uses) + a short OTP validity
   window (the `"Phone"` token provider's ~3-minute default step) + single-use consumption (the
   `Huia.PhoneVerification` cookie is signed out once a sign-in completes). Note: the OTP validity window
   isn't independently configurable without registering a custom `IUserTwoFactorTokenProvider<HuiaUser>` in
   this version.
4. **No new session-fixation risk.** `SignInManager.SignInAsync` rotates the authentication cookie for a
   passwordless sign-in exactly the same way it does for password/2FA/external sign-in.
5. **Cross-method enumeration resistance.** `PhoneLoginModel.OnPostAsync` returns the same redirect regardless
   of whether the number was a new registration, a returning passwordless user, or a collision with a
   non-passwordless account — the response never reveals which case occurred. A rate-limited request is
   *not* opaque the same way: it's sent back to `Login` with an explicit cooldown ("You can try again in N
   minutes") instead. That's still safe — `IPhoneOtpRateLimiter` throttles a number that's never had an
   account exactly the same as a registered one (see its own doc comment), so surfacing how often *this
   number* has been requesting codes reveals nothing about whether it belongs to an account. `PhoneOtpAcquireResult.RetryAfter`
   is an estimate the default `PhoneOtpRateLimiter` derives from its own per-number grant history, not a
   value read back from `System.Threading.RateLimiting` itself (its sliding-window limiters don't reliably
   populate retry-after lease metadata with `QueueLimit` 0). A resend from `PhoneLoginVerify` shows the same
   cooldown on denial rather than the old pretend-success redirect — safe there for the same reason: it's
   only reachable with a live pending cookie that already proves this number's flow was legitimately
   started, not a blind probe.

   `huia.DisableRegistration()` (see `HuiaOptions`) necessarily narrows this: with it set, a never-seen
   number (or one colliding with a non-passwordless account) is rejected outright with a distinct
   "not registered" message instead of silently creating an account and sending an OTP, since there's no
   account left for it to create. This does let an attacker distinguish "unprovisioned number" from "OTP
   sent" — an inherent, accepted trade-off for invite-only/admin-provisioned deployments, where phone numbers
   are only ever added by an administrator rather than self-service.

## Custom stores

`IHuiaStore<TApplication, TAuthorization, TScope, TToken>` composes `IHuiaPhoneNumberStore` — a fully custom
(non-EF-Core) store needs to implement `FindByNormalizedPhoneNumberAsync` to support passwordless sign-in. See
[custom-store.md](custom-store.md).
