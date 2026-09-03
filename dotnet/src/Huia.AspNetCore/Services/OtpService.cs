using System.Security.Cryptography;
using System.Text.Json;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.AspNetCore.Identity;

namespace Huia.AspNetCore.Services;

/// <summary>The outcome of verifying a one-time code.</summary>
public enum OtpVerifyResult
{
    /// <summary>The code matched and has been consumed.</summary>
    Success = 0,

    /// <summary>No code is on file for this subject.</summary>
    NotFound = 1,

    /// <summary>The code was wrong. It may still be retried until the attempt cap.</summary>
    Invalid = 2,

    /// <summary>The code has expired.</summary>
    Expired = 3,

    /// <summary>Too many wrong attempts; the code has been invalidated.</summary>
    TooManyAttempts = 4,
}

/// <summary>Issues and verifies single-use SMS one-time codes for existing accounts.</summary>
public interface IOtpService
{
    /// <summary>Generates a numeric code of the configured length.</summary>
    /// <param name="options">The tenant's phone-login options.</param>
    /// <returns>The plain code.</returns>
    string GenerateCode(PhoneLoginOptions options);

    /// <summary>Stores a hashed code for a user and returns the plain code to deliver.</summary>
    /// <param name="user">The account.</param>
    /// <param name="options">The tenant's phone-login options.</param>
    /// <returns>The plain code.</returns>
    Task<string> IssueAsync(HuiaUser user, PhoneLoginOptions options);

    /// <summary>Verifies a candidate code for a user, consuming it on success.</summary>
    /// <param name="user">The account.</param>
    /// <param name="code">The candidate code.</param>
    /// <param name="options">The tenant's phone-login options.</param>
    /// <returns>The verification outcome.</returns>
    Task<OtpVerifyResult> VerifyAsync(HuiaUser user, string code, PhoneLoginOptions options);
}

/// <summary>
/// Default <see cref="IOtpService"/>. The hashed code lives in <c>HuiaUserTokens</c> under login provider
/// <c>Huia.Passwordless</c>, token name <c>otp</c> — a table that already exists, so no migration.
/// </summary>
internal sealed class OtpService(UserManager<HuiaUser> userManager, TimeProvider timeProvider) : IOtpService
{
    private const string Provider = HuiaConstants.PasswordlessLoginProvider;
    private const string TokenName = HuiaConstants.OtpTokenName;

    public string GenerateCode(PhoneLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var max = (int)Math.Pow(10, options.CodeLength);
        var value = RandomNumberGenerator.GetInt32(0, max);
        return value.ToString().PadLeft(options.CodeLength, '0');
    }

    public async Task<string> IssueAsync(HuiaUser user, PhoneLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(options);

        var code = GenerateCode(options);
        var (hash, salt) = OtpHashing.Create(code);
        var envelope = new OtpEnvelope(hash, salt, timeProvider.GetUtcNow().Add(options.CodeLifetime), 0);

        await userManager.SetAuthenticationTokenAsync(user, Provider, TokenName, JsonSerializer.Serialize(envelope));
        return code;
    }

    public async Task<OtpVerifyResult> VerifyAsync(HuiaUser user, string code, PhoneLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(options);

        var stored = await userManager.GetAuthenticationTokenAsync(user, Provider, TokenName);
        if (string.IsNullOrEmpty(stored))
        {
            return OtpVerifyResult.NotFound;
        }

        OtpEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<OtpEnvelope>(stored);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope is null)
        {
            await userManager.RemoveAuthenticationTokenAsync(user, Provider, TokenName);
            return OtpVerifyResult.NotFound;
        }

        if (timeProvider.GetUtcNow() >= envelope.ExpiresUtc)
        {
            await userManager.RemoveAuthenticationTokenAsync(user, Provider, TokenName);
            return OtpVerifyResult.Expired;
        }

        if (OtpHashing.Verify(code, envelope.Hash, envelope.Salt))
        {
            await userManager.RemoveAuthenticationTokenAsync(user, Provider, TokenName);
            return OtpVerifyResult.Success;
        }

        var attempts = envelope.Attempts + 1;
        if (attempts >= options.MaxVerificationAttempts)
        {
            await userManager.RemoveAuthenticationTokenAsync(user, Provider, TokenName);
            return OtpVerifyResult.TooManyAttempts;
        }

        await userManager.SetAuthenticationTokenAsync(user, Provider, TokenName,
            JsonSerializer.Serialize(envelope with { Attempts = attempts }));
        return OtpVerifyResult.Invalid;
    }
}

/// <summary>The persisted one-time-code state.</summary>
/// <param name="Hash">Base64 <c>SHA-256(salt ‖ code)</c>.</param>
/// <param name="Salt">Base64 salt.</param>
/// <param name="ExpiresUtc">When the code stops being valid.</param>
/// <param name="Attempts">How many wrong guesses have been made.</param>
internal sealed record OtpEnvelope(string Hash, string Salt, DateTimeOffset ExpiresUtc, int Attempts);
