namespace Huia.Options;

/// <summary>
/// Per-tenant settings for passkey (WebAuthn / FIDO2) sign-in, enabled via
/// <see cref="HuiaTenantAuthenticationOptions.UsePasskeyLogin"/>. The relying-party identity is
/// per-tenant: <see cref="RelyingPartyId"/> is the domain a credential is bound to (left unset it is
/// the request host), and <see cref="AllowedOrigins"/> widens the origin check beyond the request
/// origin. A browser only accepts an RP id that is a registrable-domain suffix of the host serving the
/// page, so distinct per-tenant RP ids isolate credentials only when each tenant is fronted by its own
/// host at the edge; Huia validates format only.
/// </summary>
public sealed class PasskeyOptions : IHuiaOptionsSection
{
    /// <summary>How strongly a ceremony must assert user verification. Defaults to <see cref="PasskeyUserVerification.Required"/>.</summary>
    public PasskeyUserVerification UserVerification { get; set; } = PasskeyUserVerification.Required;

    /// <summary>Which class of authenticator to prefer. Defaults to <see cref="PasskeyAuthenticatorAttachment.Any"/>.</summary>
    public PasskeyAuthenticatorAttachment AuthenticatorAttachment { get; set; } = PasskeyAuthenticatorAttachment.Any;

    /// <summary>How long the browser is given to complete a ceremony. Defaults to two minutes.</summary>
    public TimeSpan AuthenticatorTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The relying-party id — the registrable domain a credential is bound to (for example
    /// <c>tenant.example.com</c>). When <see langword="null"/> (the default) the request host is used.
    /// </summary>
    public string? RelyingPartyId { get; set; }

    /// <summary>
    /// Extra origins allowed to complete a ceremony, beyond the current request origin. Leave empty for
    /// the default same-origin check.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        errors.Require(
            AuthenticatorTimeout >= TimeSpan.FromSeconds(30) && AuthenticatorTimeout <= TimeSpan.FromMinutes(10),
            HuiaOptionsValidation.Combine(path, nameof(AuthenticatorTimeout)),
            "must be between 30 seconds and 10 minutes.");

        if (!string.IsNullOrWhiteSpace(RelyingPartyId))
        {
            var validHost = Uri.CheckHostName(RelyingPartyId) is UriHostNameType.Dns
                && !RelyingPartyId.Contains('/', StringComparison.Ordinal)
                && !RelyingPartyId.Contains(':', StringComparison.Ordinal);
            errors.Require(validHost, HuiaOptionsValidation.Combine(path, nameof(RelyingPartyId)),
                "must be a bare host name (no scheme, port or path).");
        }

        for (var i = 0; i < AllowedOrigins.Count; i++)
        {
            var ok = Uri.TryCreate(AllowedOrigins[i], UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && uri.PathAndQuery == "/";
            errors.Require(ok, HuiaOptionsValidation.Combine(path, $"{nameof(AllowedOrigins)}[{i}]"),
                "must be an absolute origin such as https://app.example.com (no path).");
        }
    }
}
