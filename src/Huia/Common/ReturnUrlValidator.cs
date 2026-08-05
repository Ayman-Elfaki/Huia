using Huia.Applications;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;

namespace Huia.Common;

/// <summary>
/// Validates a <c>returnUrl</c> before redirecting to it.
/// Huia's own <c>returnUrl</c> (built by <c>/connect/authorize</c> when it needs to send the browser to a login page)
/// is legitimately a full absolute URL back to itself — but ASP.NET Core's built-in <c>LocalRedirect</c> refuses any URL with a
/// host/authority part outright, even one on the same origin, so it can't be used here
/// This performs the same anti-open-redirect check <c>LocalRedirect</c> does, extended to also accept absolute URLs that
/// resolve to the current request's own origin, or to a registered client's own origin (see
/// <see cref="ApplicationHomeUriResolver.IsTrustedReturnOriginAsync"/>) — the latter is how a HomeUri returnUrl
/// (e.g. ConfirmEmail's "Sign in" link, resolved out-of-band with no live OAuth request to resume) survives
/// the round trip through sign-in instead of being silently dropped to "/".
/// </summary>
internal static class ReturnUrlValidator
{
    public static async Task<string> ResolveAsync(HttpRequest request, string? returnUrl,
        IOpenIddictApplicationManager manager, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return "/";
        }

        // A relative/rooted path ("/foo") is safe, except the protocol-relative ("//evil.com") and
        // backslash ("/\evil.com") tricks some browsers normalize into a scheme-relative URL.
        if (returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return returnUrl;
        }

        if (returnUrl.StartsWith("~/", StringComparison.Ordinal))
        {
            return returnUrl[1..];
        }

        // UriKind.Absolute isn't restricted to http(s): on Windows, "//evil.com" and "/\evil.com" (the very
        // tricks guarded against above) parse as valid "file" URIs (UNC-path syntax) rather than failing to
        // parse — so this only treats it as a same-origin/registered-client candidate for the schemes those
        // can actually be, instead of forwarding a file:// URI to the registered-origin check below.
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "/";
        }

        if (string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return returnUrl;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);

        return await ApplicationHomeUriResolver.IsTrustedReturnOriginAsync(origin, manager, ct).ConfigureAwait(false)
            ? returnUrl
            : "/";
    }
}