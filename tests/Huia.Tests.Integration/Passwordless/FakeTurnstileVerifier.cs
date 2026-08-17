using Huia.Passwordless;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>A fully controllable <see cref="ITurnstileVerifier"/> for tests — reports whatever
/// <see cref="ShouldVerify"/> is currently set to, regardless of the token/IP it's actually given.</summary>
public sealed class FakeTurnstileVerifier : ITurnstileVerifier
{
    public bool ShouldVerify { get; set; } = true;

    public Task<bool> VerifyAsync(string? token, string? remoteIpAddress,
        CancellationToken cancellationToken = default) => Task.FromResult(ShouldVerify);
}
