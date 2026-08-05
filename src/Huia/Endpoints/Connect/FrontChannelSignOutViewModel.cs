namespace Huia.Endpoints.Connect;

/// <summary>
/// Model for <c>Areas/Identity/Pages/Account/FrontChannelSignOut.cshtml</c>, rendered by
/// <see cref="Huia.Emails.RazorViewRenderer"/> from <see cref="EndSessionEndpoints"/>.
/// </summary>
internal sealed record FrontChannelSignOutViewModel(
    IReadOnlyList<string> IframeSources,
    string ContinueUrl);
