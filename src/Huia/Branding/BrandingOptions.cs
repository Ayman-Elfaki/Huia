namespace Huia.Branding;

/// <summary>
/// Branding for Huia's server-rendered Identity pages.
/// </summary>
public sealed class BrandingOptions
{
    /// <summary>
    /// UI Theme mode
    /// </summary>
    public enum ThemeMode
    {
        /// <summary>
        /// Dark theme
        /// </summary>
        Dark,

        /// <summary>
        /// Light theme
        /// </summary>
        Light,

        /// <summary>
        /// System theme 
        /// </summary>
        System
    }

    /// <summary>
    /// Shown in the browser tab and, unless <see cref="LogoUrl"/> is set, as text in the page header.
    /// </summary>
    public string Title { get; set; } = "Huia";

    /// <summary>
    /// URL of a logo image to show centered above the form, at the top of the sign-in panel. The page
    /// header always shows <see cref="Title"/> as text regardless of whether this is set.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Overrides Basecoat's <c>--primary</c> CSS custom property (any valid CSS color: hex, oklch, etc.),
    /// applied in both light and dark mode. Leave unset to use Basecoat's default.
    /// </summary>
    public string? PrimaryColor { get; set; }

    /// <summary>
    /// URL of a favicon to use instead of Huia's bundled default.
    /// </summary>
    public string? FaviconUrl { get; set; }

    /// <summary>
    /// URL of a background image covering the full page, behind the sign-in panel (rendered
    /// <c>background-size: cover; background-position: center;</c>). Leave unset for no background image —
    /// just the page's plain background color.
    /// </summary>
    public string? BackgroundImageUrl { get; set; }

    /// <summary>
    /// Raw CSS injected verbatim into a <c>&lt;style&gt;</c> block on every Identity page, after Huia's
    /// own stylesheet and after <see cref="PrimaryColor"/>/<see cref="BackgroundImageUrl"/> — use it for
    /// anything those don't already cover. Rendered unescaped: only ever set this from a trusted,
    /// developer-controlled source, never from user input.
    /// </summary>
    public string? CustomCss { get; set; }

    /// <summary>
    /// Show toggle theme button
    /// </summary>
    public bool CanChangeThemeMode { get; set; } = true;

    /// <summary>
    /// Show topbar with title
    /// </summary>
    public bool ShowTopbar  { get; set; } = true;

    /// <summary>
    /// Default theme mode
    /// </summary>
    public ThemeMode DefaultTheme { get; set; } = ThemeMode.System;
}