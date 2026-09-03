using System.Collections.Concurrent;
using Huia.AspNetCore.Emails;
using Huia.EntityFrameworkCore.Entities;

namespace Huia.IntegrationTests.Infrastructure;

/// <summary>Records the action URLs Huia would have emailed, keyed by recipient address.</summary>
public sealed class CapturingEmailSender : IHuiaEmailSender
{
    private readonly ConcurrentDictionary<string, string> _confirmations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _resets = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task SendEmailConfirmationAsync(HuiaUser user, string confirmationUrl, CancellationToken cancellationToken = default)
    {
        _confirmations[user.Email!] = confirmationUrl;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendPasswordResetAsync(HuiaUser user, string resetUrl, CancellationToken cancellationToken = default)
    {
        _resets[user.Email!] = resetUrl;
        return Task.CompletedTask;
    }

    /// <summary>The last confirmation URL sent to an address, or <see langword="null"/>.</summary>
    public string? ConfirmationUrl(string email) => _confirmations.TryGetValue(email, out var url) ? url : null;

    /// <summary>The last reset URL sent to an address, or <see langword="null"/>.</summary>
    public string? ResetUrl(string email) => _resets.TryGetValue(email, out var url) ? url : null;
}
