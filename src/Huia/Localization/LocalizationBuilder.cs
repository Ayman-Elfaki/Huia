using System.Globalization;

namespace Huia.Localization;

/// <summary>
/// Configures which cultures Huia.UI's pages and transactional emails are localized into. Reachable via
/// <c>HuiaOptions.Localization</c> inside <c>services.AddHuia(issuer, huia => {...})</c>. English
/// (<c>"en"</c>) and Arabic (<c>"ar"</c>, right-to-left) are supported out of the box; call
/// <see cref="AddCulture"/> to add more once you've supplied your own <c>HuiaResources.{culture}.resx</c>
/// (and, if you're not using <c>Huia.EntityFrameworkCore</c>'s defaults, translated copies of any custom
/// <see cref="Microsoft.AspNetCore.Identity.IdentityErrorDescriber"/> messages).
/// </summary>
public sealed class LocalizationBuilder
{
    private readonly List<CultureInfo> _cultures = [new("en"), new("ar")];

    /// <summary>
    /// The supported cultures, seeded with English and Arabic; extend with <see cref="AddCulture"/>.
    /// </summary>
    internal IReadOnlyList<CultureInfo> Cultures => _cultures;

    /// <summary>
    /// The culture requests default to when none of <see cref="Cultures"/> was otherwise requested.
    /// Default: <c>"en"</c>.
    /// </summary>
    public string DefaultCulture { get; private set; } = "en";

    /// <summary>
    /// Adds a culture to the set Huia's pages request localized strings for, if not already present.
    /// </summary>
    public void AddCulture(string cultureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName);

        if (_cultures.All(culture => !culture.Name.Equals(cultureName, StringComparison.OrdinalIgnoreCase)))
        {
            _cultures.Add(new CultureInfo(cultureName));
        }
    }

    /// <summary>
    /// Overrides <see cref="DefaultCulture"/>. Must be (or be added via) one of <see cref="Cultures"/> —
    /// call <see cref="AddCulture"/> first if it isn't <c>"en"</c> or <c>"ar"</c>.
    /// </summary>
    public void SetDefaultCulture(string cultureName) => DefaultCulture = cultureName;
}
