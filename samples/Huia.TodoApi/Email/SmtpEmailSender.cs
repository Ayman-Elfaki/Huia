using Huia.Emails;
using Huia.Identity;
using Huia.Localization;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Localization;
using MimeKit;

namespace Huia.TodoApi.Email;

/// <summary>
/// Sends Huia's transactional emails over SMTP, rendered with <see cref="HuiaEmailTemplate"/>. Points at
/// Mailpit in this sample (see <c>Oidc:Mail</c> configuration, populated from the <c>mailpit</c> Aspire
/// resource's connection string) so registration/password-reset mail can be inspected in Mailpit's web UI
/// instead of actually being delivered.
/// </summary>
public sealed class SmtpEmailSender(
    HuiaEmailTemplate template,
    IStringLocalizer<HuiaResources> localizer,
    IConfiguration configuration) : IEmailSender<HuiaUser>
{
    public async Task SendConfirmationLinkAsync(HuiaUser user, string email, string confirmationLink) =>
        await SendAsync(email, localizer["EmailConfirmationHeading"],
            await template.ConfirmationLink(confirmationLink));

    public async Task SendPasswordResetLinkAsync(HuiaUser user, string email, string resetLink) =>
        await SendAsync(email, localizer["EmailResetPasswordHeading"], await template.PasswordResetLink(resetLink));

    public async Task SendPasswordResetCodeAsync(HuiaUser user, string email, string resetCode) =>
        await SendAsync(email, localizer["EmailResetPasswordHeading"], await template.PasswordResetCode(resetCode));

    private async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var (host, port) = GetSmtpEndpoint();

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(configuration["Mail:From"] ?? "no-reply@huia.sample"));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.None);
        await client.SendAsync(message);
        await client.DisconnectAsync(quit: true);
    }

    private (string Host, int Port) GetSmtpEndpoint()
    {
        // Aspire injects this from the "mailpit" resource's connection string. Community Toolkit's MailPit
        // integration formats it as "Endpoint=smtp://host:port" (a key=value connection string), not a bare
        // URI, so pull the "Endpoint" segment out before parsing it as one.
        var connectionString = configuration.GetConnectionString("mailpit")
                               ?? throw new InvalidOperationException(
                                   "No 'mailpit' connection string configured. Add a reference to the mailpit Aspire resource.");

        var endpoint = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair[1])
            .FirstOrDefault() ?? connectionString;

        var uri = new Uri(endpoint);
        return (uri.Host, uri.Port);
    }
}