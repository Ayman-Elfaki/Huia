using Microsoft.IdentityModel.Tokens;

namespace Huia.Keys;

/// <summary>In-memory representation of a signing key for a tenant.</summary>
public sealed class HuiaSigningKeyMaterial
{
    /// <summary>The JWK key identifier.</summary>
    public required string KeyId { get; init; }

    /// <summary>Current lifecycle stage.</summary>
    public required HuiaSigningKeyStatus Status { get; init; }

    /// <summary>The public key as a JSON Web Key.</summary>
    public required JsonWebKey PublicJwk { get; init; }

    /// <summary>The underlying security key.</summary>
    public required SecurityKey SecurityKey { get; init; }

    /// <summary>Signing credentials for token minting, or <see langword="null"/> when private key is not available.</summary>
    public required SigningCredentials? SigningCredentials { get; init; }
}
