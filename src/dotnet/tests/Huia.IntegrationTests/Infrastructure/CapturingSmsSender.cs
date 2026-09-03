using System.Collections.Concurrent;
using Huia.AspNetCore.Services;

namespace Huia.IntegrationTests.Infrastructure;

/// <summary>Test double for <see cref="ISmsSender"/> that records the last code sent to each number.</summary>
public sealed class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentDictionary<string, string> _codes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public Task<bool> SendOtpAsync(string tenantId, string phoneNumber, string code, CancellationToken cancellationToken = default)
    {
        _codes[phoneNumber] = code;
        return Task.FromResult(true);
    }

    /// <summary>The last code sent to a number, or <see langword="null"/> if none.</summary>
    /// <param name="phoneNumber">The E.164 number.</param>
    /// <returns>The code or <see langword="null"/>.</returns>
    public string? LastCode(string phoneNumber) => _codes.TryGetValue(phoneNumber, out var code) ? code : null;
}
