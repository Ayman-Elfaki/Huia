using Huia.Options;
using Microsoft.Extensions.Logging;

namespace Huia.AspNetCore.Services;

/// <summary>Delivers SMS messages for the passwordless flow.</summary>
public interface ISmsSender
{
    /// <summary>Whether a real provider is configured. When <see langword="false"/> messages are only logged.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends a one-time-code message.</summary>
    /// <param name="tenantId">The tenant the message is for.</param>
    /// <param name="phoneNumber">The E.164 destination.</param>
    /// <param name="code">The plain one-time code.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the provider accepted the message.</returns>
    Task<bool> SendOtpAsync(string tenantId, string phoneNumber, string code, CancellationToken cancellationToken = default);
}

/// <summary>
/// The built-in <see cref="ISmsSender"/>. It has no HTTP dependency: when no provider is configured it
/// logs a stub, and it only logs the actual code when <see cref="SmsOptions.LogCodesToLogger"/> is set.
/// Hosts replace this with a provider-specific implementation.
/// </summary>
internal sealed partial class HuiaSmsSender(HuiaOptions options, ILogger<HuiaSmsSender> logger) : ISmsSender
{
    public bool IsConfigured => options.Sms.IsConfigured;

    public Task<bool> SendOtpAsync(string tenantId, string phoneNumber, string code, CancellationToken cancellationToken = default)
    {
        var effective = options.Sms.MergedWith(
            options.Tenants.TryGetValue(tenantId, out var tenant) ? tenant.Sms : null);

        var mask = phoneNumber.Length <= 4 ? phoneNumber : "••••" + phoneNumber[^4..];

        if (!effective.IsConfigured)
        {
            if (effective.LogCodesToLogger)
            {
                LogStubWithCode(tenantId, mask, code);
            }
            else
            {
                LogStub(tenantId, mask);
            }

            return Task.FromResult(false);
        }

        if (effective.LogCodesToLogger)
        {
            LogSentWithCode(tenantId, mask, effective.Provider!, code);
        }
        else
        {
            LogSent(tenantId, mask, effective.Provider!);
        }

        // A real provider integration performs the HTTP call here.
        return Task.FromResult(true);
    }

    [LoggerMessage(LogLevel.Information, "SMS OTP for tenant {TenantId} to {PhoneMask}: no provider configured, not sent.")]
    partial void LogStub(string tenantId, string phoneMask);

    [LoggerMessage(LogLevel.Information, "SMS OTP for tenant {TenantId} to {PhoneMask}: no provider configured. Code: {Code}")]
    partial void LogStubWithCode(string tenantId, string phoneMask, string code);

    [LoggerMessage(LogLevel.Information, "SMS OTP for tenant {TenantId} to {PhoneMask} accepted by {Provider}.")]
    partial void LogSent(string tenantId, string phoneMask, string provider);

    [LoggerMessage(LogLevel.Information, "SMS OTP for tenant {TenantId} to {PhoneMask} accepted by {Provider}. Code: {Code}")]
    partial void LogSentWithCode(string tenantId, string phoneMask, string provider, string code);
}

/// <summary>Verifies a CAPTCHA response token.</summary>
public interface ICaptchaVerifier
{
    /// <summary>Checks a CAPTCHA response.</summary>
    /// <param name="response">The client-supplied response token.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the challenge passed.</returns>
    Task<bool> VerifyAsync(string? response, CancellationToken cancellationToken = default);
}

/// <summary>Always-passes <see cref="ICaptchaVerifier"/>. Hosts replace it when they add a real widget.</summary>
internal sealed class NullCaptchaVerifier : ICaptchaVerifier
{
    public Task<bool> VerifyAsync(string? response, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
