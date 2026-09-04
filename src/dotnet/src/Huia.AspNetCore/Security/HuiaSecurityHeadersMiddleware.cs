using System.Security.Cryptography;
using Huia.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Huia.AspNetCore.Security;

/// <summary>
/// Emits the Content-Security-Policy and companion hardening headers. Registered by <c>UseHuia()</c>
/// only when <c>AddHuiaSecurityHeaders()</c> was called; it sits right after tenant resolution and
/// before routing so a per-tenant policy could be built from branding.
/// </summary>
internal sealed class HuiaSecurityHeadersMiddleware(RequestDelegate next, IOptions<HuiaSecurityHeadersOptions> options, HuiaOptions huiaOptions)
{
    private readonly HuiaSecurityHeadersOptions _options = options.Value;

    // Sign-in and external-login form POSTs are redirected off-origin — the login form back to the
    // requesting client's redirect_uri, the external-provider challenge to the upstream IdP — so every
    // configured client and provider origin has to be allowed by `form-action` or Chromium blocks it.
    private readonly string[] _clientFormActionOrigins =
    [
        .. huiaOptions.Tenants.Values
            .SelectMany(tenant => tenant.Clients
                .SelectMany(client => client.RedirectUris.Concat(client.PostLogoutRedirectUris).Concat(client.HomeUris))
                .Concat(tenant.Authentication.External?.Providers
                    .Where(provider => provider.Authority is not null)
                    .Select(provider => new Uri(provider.Authority!, UriKind.Absolute)) ?? []))
            .Where(uri => uri.IsAbsoluteUri)
            .Select(uri => uri.GetLeftPart(UriPartial.Authority))
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    public async Task InvokeAsync(HttpContext context, IHuiaCspNonce nonce)
    {
        var nonceValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        if (nonce is HuiaCspNonce mutable)
        {
            mutable.Value = nonceValue;
        }

        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] = BuildContentSecurityPolicy(nonceValue);
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = _options.ReferrerPolicy;
        headers["Permissions-Policy"] = _options.PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        if (context.Request.IsHttps && !huiaOptions.DisableTransportSecurityRequirement)
        {
            headers["Strict-Transport-Security"] = _options.StrictTransportSecurity;
        }

        await next(context);
    }

    private StringValues BuildContentSecurityPolicy(string nonce)
    {
        var scriptSrc = _options.DisableScripts
            ? "'none'"
            : Join("'self'",
                _options.AppendNonceToInlineScripts ? $"'nonce-{nonce}'" : null,
                _options.AdditionalScriptSrc);

        var styleSrc = Join("'self'", $"'nonce-{nonce}'", _options.AdditionalStyleSrc);
        var imgSrc = Join("'self'", "data:", _options.AdditionalImageSrc);
        var connectSrc = Join("'self'", null, _options.AdditionalConnectSrc);
        var formAction = Join("'self'", null, _clientFormActionOrigins.Concat(_options.AdditionalFormActionSrc));

        return string.Join("; ",
        [
            "default-src 'self'",
            $"script-src {scriptSrc}",
            $"style-src {styleSrc}",
            $"img-src {imgSrc}",
            $"connect-src {connectSrc}",
            "font-src 'self'",
            "base-uri 'self'",
            $"form-action {formAction}",
            $"frame-ancestors {_options.FrameAncestors}",
        ]);
    }

    private static string Join(string first, string? second, IEnumerable<string> rest)
    {
        var parts = new List<string> { first };
        if (!string.IsNullOrEmpty(second))
        {
            parts.Add(second);
        }

        parts.AddRange(rest.Where(static s => !string.IsNullOrWhiteSpace(s)));
        return string.Join(' ', parts);
    }
}
