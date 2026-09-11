using System.Security.Cryptography;
using System.Text.Json;
using Huia.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace Huia.OpenId.Flows;

/// <summary>Wraps and unwraps <see cref="AuthFlowState"/> as an opaque token, and vets return URLs.</summary>
public interface IReturnUrlProtector
{
    /// <summary>Serializes and protects a flow state into a URL-safe token.</summary>
    /// <param name="state">The state to carry.</param>
    /// <returns>An opaque token.</returns>
    string Tokenize(AuthFlowState state);

    /// <summary>Reads a token produced by <see cref="Tokenize"/>.</summary>
    /// <param name="token">The token, or <see langword="null"/>.</param>
    /// <returns>The state, or <see langword="null"/> when the token is missing or invalid.</returns>
    AuthFlowState? Read(string? token);

    /// <summary>Returns <paramref name="returnUrl"/> when it is a safe local URL, otherwise the tenant root.</summary>
    /// <param name="returnUrl">The candidate URL.</param>
    /// <param name="context">The current request (for the path base).</param>
    /// <returns>A safe local URL.</returns>
    string SanitizeReturnUrl(string? returnUrl, HttpContext context);
}

/// <summary>Data Protection-backed <see cref="IReturnUrlProtector"/>.</summary>
internal sealed class ReturnUrlProtector : IReturnUrlProtector
{
    private readonly IDataProtector _protector;

    public ReturnUrlProtector(IDataProtectionProvider provider, HuiaOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        _protector = provider.CreateProtector("Huia.Flow.v1");
    }

    public string Tokenize(AuthFlowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = JsonSerializer.SerializeToUtf8Bytes(state);
        return Base64UrlEncoder.Encode(_protector.Protect(json));
    }

    public AuthFlowState? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var json = _protector.Unprotect(Base64UrlEncoder.DecodeBytes(token));
            return JsonSerializer.Deserialize<AuthFlowState>(json);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    public string SanitizeReturnUrl(string? returnUrl, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.IsNullOrEmpty(returnUrl) &&
            Uri.TryCreate(returnUrl, UriKind.Relative, out _) &&
            returnUrl.StartsWith('/') &&
            !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return returnUrl;
        }

        var pathBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value! : "/";
        return pathBase.EndsWith('/') ? pathBase : pathBase + "/";
    }
}
