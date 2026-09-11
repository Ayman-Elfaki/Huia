namespace Huia.Emails;

/// <summary>The view model for every Huia transactional email. Kept flat so the email views need no <c>@inject</c>.</summary>
/// <param name="Subject">The message subject.</param>
/// <param name="Heading">The in-body heading.</param>
/// <param name="Body">The body paragraph (already localized and formatted).</param>
/// <param name="ButtonText">The call-to-action label.</param>
/// <param name="ButtonUrl">The call-to-action URL (absolute).</param>
/// <param name="LinkFallback">Text shown before the raw URL for clients that do not render the button.</param>
/// <param name="BrandName">The tenant display name shown in the header.</param>
/// <param name="AccentColor">The accent colour for the button and rule.</param>
/// <param name="Language">The BCP-47 language tag for the <c>lang</c> attribute.</param>
/// <param name="Direction"><c>ltr</c> or <c>rtl</c>.</param>
/// <param name="LogoUrl">Absolute URL of the tenant's branding logo, shown in the header in place of
/// <paramref name="BrandName"/>. <see langword="null"/> when the tenant has no logo configured.</param>
public sealed record EmailModel(
    string Subject,
    string Heading,
    string Body,
    string ButtonText,
    string ButtonUrl,
    string LinkFallback,
    string BrandName,
    string AccentColor,
    string Language,
    string Direction,
    string? LogoUrl = null);
