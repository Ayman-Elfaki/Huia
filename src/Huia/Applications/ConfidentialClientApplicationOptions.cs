namespace Huia.Applications;

/// <summary>
/// Base configuration for client types that can hold a secret (server-rendered web apps, machine-to-machine clients).
/// Public clients (SPA, native, device) never get a secret and always require PKCE instead.
/// </summary>
public abstract class ConfidentialClientApplicationOptions : ClientApplicationOptions
{
    /// <summary>
    /// The client secret, in plain text. Huia hashes it before it reaches the OpenIddict store.
    /// </summary>
    public string? ClientSecret { get; private set; }

    /// <summary>
    /// Sets <see cref="ClientSecret"/>. Required for every confidential client.
    /// </summary>
    public void SetClientSecret(string clientSecret) => ClientSecret = clientSecret;

    internal override void Validate(string applicationKind)
    {
        base.Validate(applicationKind);

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                $"The {applicationKind} application '{ClientId}' was registered without a client secret. Call SetClientSecret(...) inside the configuration callback.");
        }
    }
}