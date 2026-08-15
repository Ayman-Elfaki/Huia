namespace Huia.Sms;

/// <summary>
/// Delivers passwordless-phone-sign-in OTP codes. Register your own implementation before calling
/// <c>AddHuia</c> to actually send SMS (e.g. via Twilio) — the default, <c>NoOpSmsSender</c>, just logs.
/// Mirrors <see cref="Microsoft.AspNetCore.Identity.IEmailSender{TUser}"/>'s shape.
/// </summary>
public interface ISmsSender<in TUser> where TUser : class
{
    /// <summary>
    /// Sends <paramref name="code"/> to <paramref name="phoneNumberE164"/> (already normalized to E.164) as
    /// a one-time sign-in code for <paramref name="user"/>.
    /// </summary>
    Task SendOtpAsync(TUser user, string phoneNumberE164, string code);
}
