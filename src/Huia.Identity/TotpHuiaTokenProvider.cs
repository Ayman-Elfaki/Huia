using System.Globalization;
using Huia.Stores;

namespace Huia.Identity;

/// <summary>
/// The <see cref="HuiaTokenProviders.Authenticator"/> provider — standard RFC 6238 TOTP (30-second step,
/// HMACSHA1, 6 digits — the parameters every authenticator app assumes) against an <c>AuthenticatorKey</c>
/// persisted via <see cref="IHuiaUserTokenStore"/> (provider <c>"Authenticator"</c>, name
/// <c>"AuthenticatorKey"</c>) — provisioned by <see cref="HuiaUserManager.ResetAuthenticatorKeyAsync"/>, never
/// by this class. Implemented directly against <see cref="Rfc6238Totp"/>/<see cref="Base32"/> — no external
/// package.
/// </summary>
/// <remarks>
/// Security review flag: RFC 6238 correctness (30-second step, ±1 step clock-skew tolerance, HMACSHA1, base32
/// key encoding) and the constant-time comparison in <see cref="Rfc6238Totp.CodesEqual"/> are exactly what the
/// plan's risk notes call out for a <c>/security-review</c> pass before this ships — a subtly wrong step
/// window or truncation would silently make every authenticator app produce mismatched codes, and a
/// too-wide clock-skew tolerance would over-accept.
/// </remarks>
public sealed class TotpHuiaTokenProvider(IHuiaUserTokenStore tokenStore) : IHuiaTokenProvider
{
    internal const string AuthenticatorKeyTokenName = "AuthenticatorKey";

    private const int StepSeconds = 30;

    /// <inheritdoc />
    public string Name => HuiaTokenProviders.Authenticator;

    /// <inheritdoc />
    public async Task<string> GenerateAsync(HuiaUser user, string purpose, CancellationToken cancellationToken)
    {
        var key = await tokenStore.GetTokenAsync(user, Name, AuthenticatorKeyTokenName, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException(
                "No authenticator key has been set for this user — call ResetAuthenticatorKeyAsync first.");
        }

        var code = Rfc6238Totp.ComputeCode(Base32.Decode(key), CurrentStep(), modifier: null);
        return code.ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(HuiaUser user, string purpose, string token, CancellationToken cancellationToken)
    {
        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var submittedCode) ||
            token.Length != 6)
        {
            return false;
        }

        var key = await tokenStore.GetTokenAsync(user, Name, AuthenticatorKeyTokenName, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var securityToken = Base32.Decode(key);
        var step = CurrentStep();

        // One step of clock-skew tolerance either side of "now", the conventional allowance for a real
        // authenticator app's clock drifting slightly from the server's.
        for (var offset = -1; offset <= 1; offset++)
        {
            var expectedCode = Rfc6238Totp.ComputeCode(securityToken, step + offset, modifier: null);
            if (Rfc6238Totp.CodesEqual(expectedCode, submittedCode))
            {
                return true;
            }
        }

        return false;
    }

    private static long CurrentStep() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
}
