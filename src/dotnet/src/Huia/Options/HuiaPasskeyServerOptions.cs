namespace Huia.Options;

/// <summary>
/// Host-wide relying-party settings for passkey (WebAuthn / FIDO2) ceremonies, configured via
/// <see cref="HuiaOptionsBuilder.ConfigurePasskeys"/>. Shared across every tenant because Huia serves
/// all tenants from one origin under the base-path strategy; per-tenant availability and policy live
/// on <see cref="PasskeyOptions"/>.
/// </summary>
public sealed class HuiaPasskeyServerOptions : IHuiaOptionsSection
{
    /// <summary>
    /// The relying-party id — the registrable domain a credential is bound to (for example
    /// <c>id.example.com</c>). When <see langword="null"/> (the default) the request host is used, which
    /// is correct for a single-host deployment; set it explicitly behind a proxy or when serving several
    /// hostnames that should share credentials.
    /// </summary>
    public string? RelyingPartyId { get; set; }

    /// <summary>
    /// The human-readable relying-party name shown by the authenticator. When <see langword="null"/> the
    /// resolved tenant's display name is used, falling back to <c>Huia</c>.
    /// </summary>
    public string? RelyingPartyName { get; set; }

    /// <summary>
    /// Extra origins allowed to complete a ceremony, beyond the current request origin. Leave empty for
    /// the default same-origin check; add entries only when the account UI is embedded on, or shared
    /// with, another origin.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(RelyingPartyId))
        {
            var valid = Uri.CheckHostName(RelyingPartyId) is UriHostNameType.Dns
                && !RelyingPartyId.Contains('/', StringComparison.Ordinal)
                && !RelyingPartyId.Contains(':', StringComparison.Ordinal);
            errors.Require(valid, HuiaOptionsValidation.Combine(path, nameof(RelyingPartyId)),
                "must be a bare host name (no scheme, port or path).");
        }

        for (var i = 0; i < AllowedOrigins.Count; i++)
        {
            var origin = AllowedOrigins[i];
            var ok = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && uri.PathAndQuery == "/";
            errors.Require(ok, HuiaOptionsValidation.Combine(path, $"{nameof(AllowedOrigins)}[{i}]"),
                "must be an absolute origin such as https://app.example.com (no path).");
        }
    }
}
