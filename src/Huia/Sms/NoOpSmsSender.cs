using Huia.Common;
using Huia.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Huia.Sms;

/// <summary>
/// Default <see cref="ISmsSender{TUser}"/> registered by <c>AddHuia</c> when
/// <c>huia.Authentication.UsePasswordlessFlow()</c> is enabled: logs instead of sending an SMS. Register
/// your own <see cref="ISmsSender{TUser}"/> implementation before calling <c>AddHuia</c> to actually deliver
/// codes.
/// </summary>
/// <remarks>
/// Unlike <c>NoOpEmailSender</c> (which always logs the full link/code — an unset email sender has no other
/// dev-mode delivery channel, and a confirmation link isn't itself a durable credential once used), the OTP
/// code here is only logged when <see cref="IHostEnvironment.IsDevelopment"/> is true. An OTP is a live,
/// reusable-until-expiry secret; logging it unconditionally risks a real one landing in production logs if a
/// consumer simply forgot to register a real <see cref="ISmsSender{TUser}"/> in a non-Development
/// environment. The phone number is masked (see <see cref="PhoneNumberMasker"/>) either way.
/// </remarks>
internal sealed class NoOpSmsSender(ILogger<NoOpSmsSender> logger, IHostEnvironment environment)
    : ISmsSender<HuiaUser>
{
    public Task SendOtpAsync(HuiaUser user, string phoneNumberE164, string code)
    {
        var masked = PhoneNumberMasker.Mask(phoneNumberE164);

        if (environment.IsDevelopment())
        {
            logger.LogWarning("No ISmsSender<HuiaUser> is registered. OTP for {MaskedPhoneNumber}: {Code}",
                masked, code);
        }
        else
        {
            logger.LogWarning(
                "No ISmsSender<HuiaUser> is registered; an OTP for {MaskedPhoneNumber} was not delivered.",
                masked);
        }

        return Task.CompletedTask;
    }
}
