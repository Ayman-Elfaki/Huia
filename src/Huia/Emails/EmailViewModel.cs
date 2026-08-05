namespace Huia.Emails;


/// <summary>
/// Model for <c>Identity/EmailTemplates/EmailBody.cshtml</c>, rendered by <see cref="RazorViewRenderer"/>.
/// </summary>
public sealed record EmailViewModel(
    string Heading,
    string IntroText,
    string? ButtonText,
    string? ButtonUrl,
    string? Code);