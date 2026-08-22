using Huia.Identity;
using Microsoft.Extensions.Logging;

namespace Huia.Emails;

/// <summary>
/// Default <see cref="IEmailSender{TUser}"/> registered by <c>AddHuia</c>: logs the link/code instead of
/// sending an email. Register your own <see cref="IEmailSender{TUser}"/> implementation before calling
/// <c>AddHuia</c> to actually deliver mail.
/// </summary>
internal sealed class NoOpEmailSender(ILogger<NoOpEmailSender> logger) : IEmailSender<HuiaUser>
{
    public Task SendConfirmationLinkAsync(HuiaUser user, string email, string confirmationLink)
    {
        logger.LogWarning(
            "No IEmailSender<HuiaUser> is registered. Email confirmation link for {Email}: {Link}", email, confirmationLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(HuiaUser user, string email, string resetLink)
    {
        logger.LogWarning(
            "No IEmailSender<HuiaUser> is registered. Password reset link for {Email}: {Link}", email, resetLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(HuiaUser user, string email, string resetCode)
    {
        logger.LogWarning(
            "No IEmailSender<HuiaUser> is registered. Password reset code for {Email}: {Code}", email, resetCode);
        return Task.CompletedTask;
    }
}