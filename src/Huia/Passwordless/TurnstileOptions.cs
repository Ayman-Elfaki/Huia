namespace Huia.Passwordless;

/// <summary>
/// The Cloudflare Turnstile site key/secret key pair a <c>PasswordlessFlowOptions.UseTurnstile</c> call
/// configured. Get a pair from the Cloudflare dashboard (dash.cloudflare.com → Turnstile).
/// </summary>
public sealed class TurnstileOptions
{
    internal TurnstileOptions(string siteKey, string secretKey)
    {
        SiteKey = siteKey;
        SecretKey = secretKey;
    }

    /// <summary>The public site key the phone sign-in form's widget renders with.</summary>
    public string SiteKey { get; }

    /// <summary>The private secret key <see cref="CloudflareTurnstileVerifier"/> authenticates with when
    /// verifying a submitted token against Cloudflare's siteverify endpoint. Never sent to the browser.</summary>
    public string SecretKey { get; }
}
