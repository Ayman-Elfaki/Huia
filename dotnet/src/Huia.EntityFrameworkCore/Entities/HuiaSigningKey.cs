namespace Huia.EntityFrameworkCore.Entities;

/// <summary>The lifecycle stage of a tenant signing key.</summary>
public enum HuiaSigningKeyStatus
{
    /// <summary>Created and published in the JWKS, but not yet used for signing (propagation window).</summary>
    Pending = 0,

    /// <summary>The current signing key for its tenant. Exactly one key per tenant is active at a time.</summary>
    Active = 1,

    /// <summary>No longer signing, still published so previously issued tokens keep validating.</summary>
    Rotated = 2,

    /// <summary>Unpublished and awaiting permanent deletion after the grace period.</summary>
    Retired = 3,
}

/// <summary>
/// One RSA signing key for one tenant. The public half is stored as a JWK; the private half is stored
/// wrapped by ASP.NET Core Data Protection and is never exposed outside the key services.
/// </summary>
public class HuiaSigningKey
{
    /// <summary>Surrogate key.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The tenant this key belongs to.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The JWK <c>kid</c> advertised in the JWKS and stamped into token headers.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The JWS algorithm (for example <c>RS256</c>).</summary>
    public string Algorithm { get; set; } = "RS256";

    /// <summary>The public key as a JSON Web Key document.</summary>
    public string PublicJwkJson { get; set; } = string.Empty;

    /// <summary>The private key (PKCS#8) wrapped with Data Protection, Base64-encoded.</summary>
    public string WrappedPrivateKey { get; set; } = string.Empty;

    /// <summary>Current lifecycle stage.</summary>
    public HuiaSigningKeyStatus Status { get; set; } = HuiaSigningKeyStatus.Pending;

    /// <summary>When the key was generated (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The earliest time the key may become <see cref="HuiaSigningKeyStatus.Active"/>.</summary>
    public DateTimeOffset ActivateAt { get; set; }

    /// <summary>When the key stopped signing, if it has (UTC).</summary>
    public DateTimeOffset? RotatedAt { get; set; }

    /// <summary>When the key was unpublished, if it has been (UTC).</summary>
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>Whether the key is currently advertised in the tenant JWKS.</summary>
    public bool IsPublished => Status is HuiaSigningKeyStatus.Pending or HuiaSigningKeyStatus.Active or HuiaSigningKeyStatus.Rotated;
}
