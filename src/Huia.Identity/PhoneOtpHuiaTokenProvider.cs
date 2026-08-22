using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Huia.Identity;

/// <summary>
/// The <see cref="HuiaTokenProviders.Phone"/> provider — a numeric one-time code derived deterministically
/// from <see cref="HuiaUser.SecurityStamp"/> and the current time step (the same
/// security-stamp/time-step-keyed HOTP construction ASP.NET Core Identity's own phone/email token providers
/// use — see <see cref="Rfc6238Totp"/>), so no server-side state needs to be persisted between generating a
/// code and later validating it: recomputing the code for the same (security stamp, time step) always
/// reproduces it. Unlike ASP.NET Core Identity's built-in phone provider (hardcoded to a 3-minute step), the
/// step length is <see cref="HuiaTokenOptions.PhoneOtpLifetime"/> — independently configurable, fixing the
/// limitation docs/passwordless.md documents.
/// </summary>
/// <remarks>
/// Security review flag: clock-skew tolerance (one step either side of "now") and the HMACSHA1 construction
/// itself are worth a <c>/security-review</c> pass — a wider tolerance window trades security margin for
/// forgiving a slow SMS delivery.
/// </remarks>
public sealed class PhoneOtpHuiaTokenProvider(IOptions<HuiaIdentityOptions> options) : IHuiaTokenProvider
{
    /// <inheritdoc />
    public string Name => HuiaTokenProviders.Phone;

    /// <inheritdoc />
    public Task<string> GenerateAsync(HuiaUser user, string purpose, CancellationToken cancellationToken)
    {
        var step = CurrentStep();
        var code = ComputeCode(user, purpose, step);
        return Task.FromResult(code.ToString("D6", CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public Task<bool> ValidateAsync(HuiaUser user, string purpose, string token, CancellationToken cancellationToken)
    {
        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var submittedCode) ||
            token.Length != 6)
        {
            return Task.FromResult(false);
        }

        var step = CurrentStep();

        // Tolerates the code having been generated in the immediately preceding step, so a code shown right
        // before a rollover doesn't fail purely from network/typing delay.
        for (var offset = 0; offset <= 1; offset++)
        {
            var expectedCode = ComputeCode(user, purpose, step - offset);
            if (Rfc6238Totp.CodesEqual(expectedCode, submittedCode))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    private long CurrentStep()
    {
        var stepSeconds = Math.Max(1, (long)options.Value.Tokens.PhoneOtpLifetime.TotalSeconds);
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() / stepSeconds;
    }

    private static int ComputeCode(HuiaUser user, string purpose, long step)
    {
        using var sha1 = SHA1.Create();
        var securityToken = sha1.ComputeHash(Encoding.Unicode.GetBytes(user.SecurityStamp));
        var modifier = $"Huia.Phone:{purpose}:{user.Id}";
        return Rfc6238Totp.ComputeCode(securityToken, step, modifier);
    }
}
