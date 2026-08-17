using Huia.Passwordless;

namespace Huia.Tests.Unit.Passwordless;

/// <summary>
/// <c>AddHuia</c> registers these two whenever <c>PasswordlessFlowOptions.EnableIpRateLimiting</c>/
/// <c>UseTurnstile</c> weren't called, so <c>PhoneLoginModel</c> can call both unconditionally without
/// branching on whether either feature is actually turned on — see their own doc comments.
/// </summary>
public class NoOpImplementationsTests
{
    [Fact]
    public async Task NoOpPhoneIpRateLimiter_AlwaysAcquires()
    {
        var limiter = new NoOpPhoneIpRateLimiter();

        var first = await limiter.TryAcquireAsync("203.0.113.1");
        var second = await limiter.TryAcquireAsync("203.0.113.1");
        var third = await limiter.TryAcquireAsync("203.0.113.1");

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.True(third.IsAcquired);
    }

    [Fact]
    public async Task NoOpTurnstileVerifier_AlwaysVerifies_EvenWithNoToken()
    {
        var verifier = new NoOpTurnstileVerifier();

        Assert.True(await verifier.VerifyAsync("a-token", "203.0.113.1"));
        Assert.True(await verifier.VerifyAsync(null, null));
    }
}
