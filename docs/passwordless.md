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

## Configuring which `IdentityOptions` apply

```csharp
huia.Authentication.UsePasswordlessFlow(configureIdentity: identity =>
{
    identity.Lockout.MaxFailedAccessAttempts = 5;
});
```

Runs directly against the same `IdentityOptions` instance ASP.NET Core Identity itself builds — unlike the
old, removed `HuiaOptions.Identity` property, changes made here actually take effect.

## Rate limiting

Configurable, per phone number, checked before any user lookup/creation — so it throttles a number with no
account at all just as well as a registered one:

```csharp
huia.Authentication.UsePasswordlessFlow(configureRateLimit: rateLimit =>
{
    rateLimit.RequestsPerMinute = 1; // default
    rateLimit.RequestsPerHour = 3;   // default
    rateLimit.RequestsPerDay = 10;   // default
});
```

The default `IPhoneOtpRateLimiter` is in-memory (per instance); register your own implementation (e.g.
Redis-backed) before calling `AddHuia` if limits need to be shared across a scaled-out deployment.

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

1. **`HuiaUser.PasswordlessLoginEnabled` is the account-linking boundary.** A `PhoneNumber` present on an
   account for any other reason (a future SMS-2FA feature, an admin-entered contact number) never implicitly
   becomes a standalone sign-in path — only an account *created through* the passwordless flow (or later
   explicitly opted in, not built in this version) is reachable via phone+OTP alone. This is deliberate:
   password knowledge and OTP-over-SMS possession are not equivalent-strength proofs. SMS is interceptable via
   SIM-swap, SS7 exploits, or a compromised carrier account — none of which require compromising the account
   holder's actual credentials. Treating "has this phone number on file" as sufficient proof to sign into an
   *existing password account* would silently add a weaker authentication path to any account that ever had a
   phone number recorded for an unrelated reason. Contrast this with [external-provider password
   linking](external-providers.md#password-confirmed-linking-on-email-collision): there, proving password
   ownership genuinely *is* an equivalent-strength proof, which is why that auto-link is safe and this one
   isn't.
2. **A phone-number collision creates a second, distinct account, never a silent merge.** If a submitted
   number matches an existing account that isn't `PasswordlessLoginEnabled`, `PhoneLoginModel` creates a new
   `HuiaUser` row for the passwordless identity — the same phone number then exists on two separate rows.
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

## Custom stores

`IHuiaStore<TApplication, TAuthorization, TScope, TToken>` composes `IHuiaPhoneNumberStore` — a fully custom
(non-EF-Core) store needs to implement `FindByNormalizedPhoneNumberAsync` to support passwordless sign-in. See
[custom-store.md](custom-store.md).
