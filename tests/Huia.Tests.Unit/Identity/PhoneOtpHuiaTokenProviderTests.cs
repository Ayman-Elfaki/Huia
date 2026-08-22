using Huia.Identity;
using Microsoft.Extensions.Options;

namespace Huia.Tests.Unit.Identity;

/// <summary>
/// Confirms <see cref="HuiaIdentityOptions.Tokens"/>'s <see cref="HuiaTokenOptions.PhoneOtpLifetime"/> is
/// actually read by <see cref="PhoneOtpHuiaTokenProvider"/> and governs the OTP's validity window end to end —
/// not just a defined-but-unused option. This is the concrete fix docs/passwordless.md calls out: unlike
/// ASP.NET Core Identity's built-in phone provider (hardcoded to a 3-minute step), a host app configuring
/// <c>huia.Identity(o => o.Tokens.PhoneOtpLifetime = ...)</c> must change how long a code stays valid.
/// </summary>
public class PhoneOtpHuiaTokenProviderTests
{
    private static PhoneOtpHuiaTokenProvider CreateProvider(TimeSpan lifetime)
    {
        var options = new HuiaIdentityOptions();
        options.Tokens.PhoneOtpLifetime = lifetime;
        return new PhoneOtpHuiaTokenProvider(Options.Create(options));
    }

    private static HuiaUser CreateUser() => new()
    {
        Id = "user-1",
        UserName = "user@example.com",
        SecurityStamp = Guid.NewGuid().ToString("N"),
    };

    [Fact]
    public void Name_IsPhone()
    {
        var provider = CreateProvider(TimeSpan.FromMinutes(5));

        Assert.Equal(HuiaTokenProviders.Phone, provider.Name);
    }

    [Fact]
    public async Task GenerateThenValidate_WithinLifetime_Succeeds()
    {
        var provider = CreateProvider(TimeSpan.FromMinutes(5));
        var user = CreateUser();

        var code = await provider.GenerateAsync(user, "purpose", CancellationToken.None);

        Assert.True(await provider.ValidateAsync(user, "purpose", code, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_CodeGeneratedForDifferentUser_Fails()
    {
        var provider = CreateProvider(TimeSpan.FromMinutes(5));
        var user = CreateUser();
        var otherUser = CreateUser();
        otherUser.Id = "user-2";

        var code = await provider.GenerateAsync(user, "purpose", CancellationToken.None);

        Assert.False(await provider.ValidateAsync(otherUser, "purpose", code, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_WrongPurpose_Fails()
    {
        var provider = CreateProvider(TimeSpan.FromMinutes(5));
        var user = CreateUser();

        var code = await provider.GenerateAsync(user, "purpose-a", CancellationToken.None);

        Assert.False(await provider.ValidateAsync(user, "purpose-b", code, CancellationToken.None));
    }

    /// <summary>
    /// The core "is <see cref="HuiaTokenOptions.PhoneOtpLifetime"/> actually enforced" check: with a
    /// non-default (1-second) lifetime configured, a code must stop validating once enough time has passed to
    /// cross two step boundaries (the provider tolerates the immediately preceding step, so a code survives
    /// one boundary but not two). If the option were merely defined and not wired into the step calculation,
    /// this would still pass at the default 5-minute cadence and never observe expiry within the test.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_NonDefaultLifetimeConfigured_CodeExpiresAfterConfiguredWindow()
    {
        var provider = CreateProvider(TimeSpan.FromSeconds(1));
        var user = CreateUser();

        var code = await provider.GenerateAsync(user, "purpose", CancellationToken.None);
        Assert.True(await provider.ValidateAsync(user, "purpose", code, CancellationToken.None));

        // Cross at least two full 1-second steps so both the code's own step and the one step of tolerance
        // have rolled past.
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        Assert.False(await provider.ValidateAsync(user, "purpose", code, CancellationToken.None));
    }
}
