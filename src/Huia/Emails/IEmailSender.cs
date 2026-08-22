using Huia.Identity;

namespace Huia.Emails;

/// <summary>
/// Sends transactional emails for a <typeparamref name="TUser"/> — email confirmation and password reset.
/// Register your own implementation before calling <c>AddHuia</c> to actually deliver mail; the default,
/// <see cref="NoOpEmailSender"/>, just logs.
/// </summary>
public interface IEmailSender<in TUser> where TUser : class
{
    /// <summary>Sends an email-confirmation link to <paramref name="email"/> for <paramref name="user"/>.</summary>
    Task SendConfirmationLinkAsync(TUser user, string email, string confirmationLink);

    /// <summary>Sends a password-reset link to <paramref name="email"/> for <paramref name="user"/>.</summary>
    Task SendPasswordResetLinkAsync(TUser user, string email, string resetLink);

    /// <summary>Sends a password-reset code to <paramref name="email"/> for <paramref name="user"/>.</summary>
    Task SendPasswordResetCodeAsync(TUser user, string email, string resetCode);
}
