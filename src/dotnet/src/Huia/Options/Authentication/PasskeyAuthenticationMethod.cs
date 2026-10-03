namespace Huia.Options;

/// <summary>
/// Passkey (WebAuthn / FIDO2) authentication method options.
/// </summary>
public class PasskeyAuthenticationMethod : HuiaAuthenticationMethod
{
    /// <inheritdoc />
    public override string MethodType => "Passkey";

    /// <summary>Relying-party user verification requirement.</summary>
    public PasskeyUserVerification UserVerification { get; set; } = PasskeyUserVerification.Required;

    /// <summary>Relying-party authenticator attachment preference.</summary>
    public PasskeyAuthenticatorAttachment AuthenticatorAttachment { get; set; } = PasskeyAuthenticatorAttachment.Any;

    /// <summary>Whether a discoverable resident key is required.</summary>
    public bool RequireResidentKey { get; set; } = true;

    /// <summary>Relying party identifier (RP ID). Uses host when unset.</summary>
    public string? RelyingPartyId { get; set; }

    /// <summary>Allowed origins for ceremonies.</summary>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>Timeout for attestation / assertion ceremonies.</summary>
    public TimeSpan AuthenticatorTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        base.Validate(path, errors);

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
            var origin = AllowedOrigins[i];
            var originPath = HuiaOptionsValidation.Combine(path, $"{nameof(AllowedOrigins)}[{i}]");
            var ok = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && uri.PathAndQuery == "/";
            errors.Require(ok, originPath, "must be an absolute origin such as https://app.example.com (no path).");
        }
    }
}
