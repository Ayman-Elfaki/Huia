namespace Huia.Security;

/// <summary>
/// Carries the per-request Content-Security-Policy nonce. Registered unconditionally so a Razor
/// <c>@inject</c> never fails; <see cref="Value"/> is the empty string when the security-headers
/// middleware is not in the pipeline.
/// </summary>
public interface IHuiaCspNonce
{
    /// <summary>The nonce for the current request, or the empty string when no CSP is being emitted.</summary>
    string Value { get; }
}

/// <summary>Mutable, request-scoped <see cref="IHuiaCspNonce"/>. Only the security-headers middleware sets it.</summary>
public sealed class HuiaCspNonce : IHuiaCspNonce
{
    /// <summary>The nonce value.</summary>
    public string Value { get; set; } = string.Empty;
}
