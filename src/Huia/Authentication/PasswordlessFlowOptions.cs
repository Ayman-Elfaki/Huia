using Huia.Common;
using Huia.Passwordless;

namespace Huia.Authentication;

/// <summary>
/// Configures passwordless phone/OTP sign-in — passed to
/// <c>huia.Authentication.UsePasswordlessFlow(passwordless => {...})</c>. See docs/passwordless.md. ASP.NET
/// Core Identity's shared <c>IdentityOptions</c> (lockout, etc.) isn't configured here — see
/// <c>HuiaOptions.Identity</c>, which applies regardless of which flow(s) are enabled.
/// </summary>
public sealed class PasswordlessFlowOptions
{
    private string? _defaultCountryCode;

    /// <summary>
    /// The per-phone-number OTP request rate limit (defaults: 1/minute, 3/hour, 10/day) — always enforced,
    /// mutate its properties directly to override the defaults. See <see cref="PhoneOtpRateLimitOptions"/>.
    /// </summary>
    public PhoneOtpRateLimitOptions RateLimit { get; } = new();

    /// <summary>Whether <see cref="EnableIpRateLimiting"/> has been called.</summary>
    internal bool IpRateLimitEnabled { get; private set; }

    /// <summary>The per-client-IP OTP request rate limit — only enforced once <see cref="EnableIpRateLimiting"/>
    /// has been called. See <see cref="PhoneIpRateLimitOptions"/>.</summary>
    internal PhoneIpRateLimitOptions IpRateLimit { get; } = new();

    /// <summary>The Cloudflare Turnstile keys <see cref="UseTurnstile"/> configured, or <see langword="null"/>
    /// if it was never called.</summary>
    internal TurnstileOptions? Turnstile { get; private set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 code (e.g. <c>"US"</c>) the phone form's country picker preselects, instead of
    /// requiring every sign-in to pick a country explicitly. Must be one of the regions libphonenumber
    /// recognizes — throws <see cref="ArgumentException"/> otherwise, since a typo here would otherwise only
    /// surface as a country that silently never appears preselected. <see langword="null"/> (the default)
    /// means no default — the picker starts unselected.
    /// </summary>
    public string? DefaultCountryCode
    {
        get => _defaultCountryCode;
        set
        {
            if (value is not null && !CountryPhoneCodeProvider.IsSupportedRegion(value))
            {
                throw new ArgumentException($"'{value}' isn't a region libphonenumber recognizes.", nameof(value));
            }

            _defaultCountryCode = value?.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Enables an additional, coarser rate limit keyed by the requester's client IP address rather than the
    /// phone number being requested — catches a script that enumerates many different phone numbers from the
    /// same source, which the per-phone-number limit alone can't (each individual number never exceeds its
    /// own limit). Off by default: a shared/NATed IP can plausibly represent many genuine users behind one
    /// address, so this is opt-in rather than always-on, and its defaults (see
    /// <see cref="PhoneIpRateLimitOptions"/>) are deliberately looser than the per-phone-number ones.
    /// </summary>
    /// <param name="configure">Overrides the per-IP limit's defaults (5/minute, 20/hour, 50/day).</param>
    public void EnableIpRateLimiting(Action<PhoneIpRateLimitOptions>? configure = null)
    {
        IpRateLimitEnabled = true;
        configure?.Invoke(IpRateLimit);
    }

    /// <summary>
    /// Requires a Cloudflare Turnstile challenge to pass before an OTP is sent — an additional, configurable
    /// layer against automated SMS-bombing scripts, on top of the per-phone-number and (if enabled) per-IP
    /// rate limits. Off by default. Get a site key/secret key pair from the Cloudflare dashboard
    /// (dash.cloudflare.com → Turnstile).
    /// </summary>
    public void UseTurnstile(string siteKey, string secretKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);
        Turnstile = new TurnstileOptions(siteKey, secretKey);
    }
}
