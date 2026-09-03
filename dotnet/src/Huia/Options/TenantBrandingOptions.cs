namespace Huia.Options;

/// <summary>Per-tenant presentation settings surfaced on the account UI and in emails.</summary>
public sealed class TenantBrandingOptions : IHuiaOptionsSection
{
    /// <summary>The tenant's display name. Defaults to the tenant identifier when unset.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Absolute or app-relative URL of the tenant logo.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Absolute or app-relative URL of the tenant favicon shown on the account UI.</summary>
    public string? FaviconUrl { get; set; }

    /// <summary>CSS colour used as the accent on the account UI (for example <c>#4f46e5</c>).</summary>
    public string? AccentColor { get; set; }

    /// <summary>URL of the tenant's support page.</summary>
    public Uri? SupportUrl { get; set; }

    /// <summary>URL of the tenant's privacy policy.</summary>
    public Uri? PrivacyUrl { get; set; }

    /// <summary>URL of the tenant's terms of service.</summary>
    public Uri? TermsUrl { get; set; }

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(AccentColor))
        {
            errors.Require(
                System.Text.RegularExpressions.Regex.IsMatch(AccentColor, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$"),
                HuiaOptionsValidation.Combine(path, nameof(AccentColor)),
                "must be a 3- or 6-digit hex colour.");
        }
    }
}
