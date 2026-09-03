namespace Huia.AspNetCore.Security;

/// <summary>
/// Presence marker. <c>AddHuiaSecurityHeaders()</c> registers it as a singleton; <c>UseHuia()</c> only
/// inserts <see cref="HuiaSecurityHeadersMiddleware"/> when it can be resolved.
/// </summary>
internal sealed class HuiaSecurityHeadersMarker;
