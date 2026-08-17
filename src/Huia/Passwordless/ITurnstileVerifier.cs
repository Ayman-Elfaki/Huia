namespace Huia.Passwordless;

/// <summary>
/// Verifies a Cloudflare Turnstile challenge token — checked in <c>PhoneLoginModel.OnPostAsync</c> as an
/// additional, configurable layer against automated SMS-bombing scripts, on top of the per-phone-number and
/// (if enabled) per-IP rate limits. Registered unconditionally by <c>AddHuia</c> — a no-op implementation
/// (always verifies) unless <c>PasswordlessFlowOptions.UseTurnstile</c> was called, so <c>PhoneLoginModel</c>
/// never needs to branch on whether the feature is actually turned on.
/// </summary>
public interface ITurnstileVerifier
{
    /// <summary>
    /// Verifies <paramref name="token"/> (the widget's <c>cf-turnstile-response</c> form field) against
    /// Cloudflare's siteverify endpoint. Returns <see langword="false"/> for a missing, expired, or otherwise
    /// invalid token — never throws for a bad token, only for a genuine failure to reach Cloudflare at all.
    /// </summary>
    /// <param name="token">The token the widget submitted, or <see langword="null"/> if it never ran.</param>
    /// <param name="remoteIpAddress">The requester's IP, passed through to Cloudflare to strengthen its own
    /// risk scoring — optional, omitted from the verification request entirely when unavailable.</param>
    Task<bool> VerifyAsync(string? token, string? remoteIpAddress, CancellationToken cancellationToken = default);
}
