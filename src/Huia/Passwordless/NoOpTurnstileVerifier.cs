namespace Huia.Passwordless;

/// <summary>
/// Registered by <c>AddHuia</c> instead of <see cref="CloudflareTurnstileVerifier"/> when
/// <c>PasswordlessFlowOptions.UseTurnstile</c> was never called — every token verifies, so
/// <c>PhoneLoginModel</c> can call <see cref="ITurnstileVerifier"/> unconditionally without branching on
/// whether the feature is actually turned on.
/// </summary>
internal sealed class NoOpTurnstileVerifier : ITurnstileVerifier
{
    public Task<bool> VerifyAsync(string? token, string? remoteIpAddress,
        CancellationToken cancellationToken = default) => Task.FromResult(true);
}
