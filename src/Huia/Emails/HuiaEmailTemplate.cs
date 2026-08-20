using Huia.Localization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Localization;

namespace Huia.Emails;

/// <summary>
/// Renders Basecoat-styled, branded HTML bodies for Huia's transactional emails (email confirmation,
/// password reset), localized into the request's current UI culture (see <c>HuiaOptions.Localization</c>).
/// Inject this into your own <see cref="Microsoft.AspNetCore.Identity.IEmailSender{TUser}"/> implementation
/// instead of hand-rolling HTML — the brand header and button color come from
/// <see cref="HuiaOptions.Branding"/>, the same options Huia.UI's pages use. <see cref="NoOpEmailSender"/>
/// doesn't use this (it only logs), but any real sender should.
/// </summary>
/// <remarks>
/// Bodies are rendered from <c>Identity/EmailTemplates/EmailBody.cshtml</c> via <see cref="RazorViewRenderer"/>,
/// the same Razor/localization pipeline Huia.UI's pages use.
/// </remarks>
public sealed class HuiaEmailTemplate(RazorViewRenderer renderer, IStringLocalizer<HuiaResources> localizer)
{
    private const string ViewPath = "/Emails/Template/EmailTemplate.cshtml";

    /// <summary>Renders the body for an email-confirmation message.</summary>
    public Task<string> ConfirmationLink(string confirmationLink) => Render(
        heading: localizer["EmailConfirmationHeading"],
        introText: localizer["EmailConfirmationIntro"],
        buttonText: localizer["EmailConfirmationButton"],
        buttonUrl: confirmationLink);

    /// <summary>Renders the body for a password-reset-link message.</summary>
    public Task<string> PasswordResetLink(string resetLink) => Render(
        heading: localizer["EmailResetPasswordHeading"],
        introText: localizer["EmailResetPasswordLinkIntro"],
        buttonText: localizer["EmailResetPasswordButton"],
        buttonUrl: resetLink);

    /// <summary>Renders the body for a password-reset-code message.</summary>
    public Task<string> PasswordResetCode(string resetCode) => Render(
        heading: localizer["EmailResetPasswordHeading"],
        introText: localizer["EmailResetPasswordCodeIntro"],
        code: resetCode);

    private Task<string> Render(string heading, string introText, string? buttonText = null, string? buttonUrl = null,
        string? code = null) =>
        renderer.RenderAsync(ViewPath, new EmailViewModel(heading, introText, buttonText, buttonUrl, code));
}