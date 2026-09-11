using Huia.Localization;
using Microsoft.Extensions.Localization;

namespace Huia.OpenId.UI;

/// <summary>Formats a "try again in …" duration using the shared <c>Common.Duration.*</c> resources.</summary>
internal static class RetryAfterText
{
    /// <summary>Renders <paramref name="value"/> as a localized "N seconds" / "N minutes" phrase.</summary>
    /// <param name="localizer">The shared-resource localizer.</param>
    /// <param name="value">The remaining time.</param>
    /// <returns>The localized phrase.</returns>
    public static string Format(IStringLocalizer<SharedResource> localizer, TimeSpan value)
    {
        var seconds = (int)Math.Ceiling(value.TotalSeconds);
        return seconds < 60
            ? localizer.GetString("Common.Duration.Seconds", seconds)
            : localizer.GetString("Common.Duration.Minutes", (int)Math.Ceiling(seconds / 60d));
    }
}
