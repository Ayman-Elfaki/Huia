using Huia.Localization;
using Microsoft.Extensions.Localization;

namespace Huia.Passwordless;

/// <summary>
/// Turns a denied <see cref="PhoneOtpAcquireResult.RetryAfter"/> into the message shown on
/// <c>PhoneLogin</c>'s (via <c>LoginModel</c>) and <c>PhoneLoginVerify</c>'s cooldown UI — shared so both
/// spots word it identically.
/// </summary>
internal static class PhoneOtpCooldownFormatter
{
    /// <summary>Formats <paramref name="retryAfter"/> as a whole-minute cooldown message, rounded up (never
    /// telling the requester they can retry sooner than they actually can) with a floor of one minute.</summary>
    public static string FormatMessage(IStringLocalizer<HuiaResources> localizer, TimeSpan retryAfter)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalMinutes));
        return minutes == 1
            ? localizer["PhoneCooldownMessageSingular"]
            : localizer["PhoneCooldownMessagePlural", minutes];
    }
}
