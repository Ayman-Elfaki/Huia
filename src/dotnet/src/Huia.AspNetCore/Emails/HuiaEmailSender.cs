using System.Globalization;
using Huia.EntityFrameworkCore.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Huia.AspNetCore.Emails;

/// <summary>Sends the Huia transactional emails (email confirmation, password reset).</summary>
public interface IHuiaEmailSender
{
    /// <summary>Sends the "confirm your email" message.</summary>
    /// <param name="user">The recipient account.</param>
    /// <param name="confirmationUrl">The absolute confirmation URL.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the send.</returns>
    Task SendEmailConfirmationAsync(HuiaUser user, string confirmationUrl, CancellationToken cancellationToken = default);

    /// <summary>Sends the "reset your password" message.</summary>
    /// <param name="user">The recipient account.</param>
    /// <param name="resetUrl">The absolute reset URL.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the send.</returns>
    Task SendPasswordResetAsync(HuiaUser user, string resetUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// MailKit-backed <see cref="IHuiaEmailSender"/>. Renders the HTML body with <see cref="RazorEmailRenderer"/>
/// and ships a <c>multipart/alternative</c> message. When no SMTP host is configured it logs and returns.
/// </summary>
internal sealed partial class HuiaEmailSender(
    RazorEmailRenderer renderer,
    HuiaOptions options,
    IMultiTenantContextAccessor tenantAccessor,
    IStringLocalizer<SharedResource> localizer,
    ILogger<HuiaEmailSender> logger) : IHuiaEmailSender
{
    public Task SendEmailConfirmationAsync(HuiaUser user, string confirmationUrl, CancellationToken cancellationToken = default) =>
        SendAsync(user, confirmationUrl, "Email.Confirm", cancellationToken);

    public Task SendPasswordResetAsync(HuiaUser user, string resetUrl, CancellationToken cancellationToken = default) =>
        SendAsync(user, resetUrl, "Email.Reset", cancellationToken);

    private async Task SendAsync(HuiaUser user, string actionUrl, string keyPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var tenantId = tenantAccessor.CurrentTenantId();
        var tenant = tenantId is not null && options.Tenants.TryGetValue(tenantId, out var t) ? t : null;
        var email = options.Email.MergedWith(tenant?.Email);
        var culture = CultureInfo.CurrentUICulture;

        var model = new EmailModel(
            Subject: localizer[$"{keyPrefix}.Subject"].Value,
            Heading: localizer[$"{keyPrefix}.Heading"].Value,
            Body: localizer.GetString($"{keyPrefix}.Body", user.FirstName ?? user.UserName ?? string.Empty).Value,
            ButtonText: localizer[$"{keyPrefix}.Button"].Value,
            ButtonUrl: actionUrl,
            LinkFallback: localizer["Email.LinkFallback"].Value,
            BrandName: tenant?.Branding.DisplayName ?? tenant?.DisplayName ?? tenantId ?? "Huia",
            AccentColor: tenant?.Branding.AccentColor ?? "#4f46e5",
            Language: culture.TwoLetterISOLanguageName,
            Direction: culture.TextInfo.IsRightToLeft ? "rtl" : "ltr");

        var html = await renderer.RenderAsync("/Emails/Views/Message.cshtml", model);
        var text = $"{model.Heading}\n\n{model.Body}\n\n{model.ButtonText}: {model.ButtonUrl}\n";

        if (!email.IsConfigured || string.IsNullOrEmpty(email.Host) || string.IsNullOrEmpty(email.FromAddress))
        {
            LogNotConfigured(user.Email, model.Subject);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(email.FromName ?? model.BrandName, email.FromAddress));
        message.To.Add(MailboxAddress.Parse(user.Email));
        message.Subject = model.Subject;
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(email.Host, email.Port,
            email.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);

        if (!string.IsNullOrEmpty(email.UserName))
        {
            await client.AuthenticateAsync(email.UserName, email.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
        LogSent(user.Email, model.Subject);
    }

    [LoggerMessage(LogLevel.Information, "Email '{Subject}' to {Recipient} not sent: no SMTP host configured.")]
    partial void LogNotConfigured(string recipient, string subject);

    [LoggerMessage(LogLevel.Information, "Email '{Subject}' sent to {Recipient}.")]
    partial void LogSent(string recipient, string subject);
}
