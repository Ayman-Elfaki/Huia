namespace Huia.AspNetCore.Flows;

/// <summary>
/// The payload carried through a multi-step sign-in as an opaque, Data Protection-wrapped token (the
/// <c>flow</c> query parameter). Sensitive values — a phone number in particular — travel here rather
/// than in a plain query string.
/// </summary>
public sealed record AuthFlowState
{
    /// <summary>Where to send the browser once the flow completes (a local URL).</summary>
    public string? ReturnUrl { get; init; }

    /// <summary>The E.164 phone number a passwordless sign-in is running for.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Identifier of a pending auto-provisioning record (number with no account yet).</summary>
    public string? PendingSignupId { get; init; }

    /// <summary>The existing user the flow resolved to (for example a blank-name account completing its profile).</summary>
    public string? UserId { get; init; }

    /// <summary>The external provider registration id, when completing an external sign-up.</summary>
    public string? ExternalProvider { get; init; }

    /// <summary>The external provider's subject identifier for the user.</summary>
    public string? ExternalProviderKey { get; init; }

    /// <summary>A display name suggested by the external provider.</summary>
    public string? ExternalDisplayName { get; init; }

    /// <summary>The upstream provider's id token, carried through profile completion for sign-out.</summary>
    public string? ExternalIdToken { get; init; }

    /// <summary>An email address suggested by the external provider (or entered by the user).</summary>
    public string? Email { get; init; }

    /// <summary>A given name carried between steps.</summary>
    public string? FirstName { get; init; }

    /// <summary>A family name carried between steps.</summary>
    public string? LastName { get; init; }
}
