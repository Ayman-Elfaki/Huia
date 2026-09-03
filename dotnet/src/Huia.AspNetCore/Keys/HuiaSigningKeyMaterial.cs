using Huia.EntityFrameworkCore.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Huia.AspNetCore.Keys;

/// <summary>A materialized signing key: the parsed public JWK plus, when available, private material.</summary>
public sealed class HuiaSigningKeyMaterial
{
    /// <summary>The JWK <c>kid</c>.</summary>
    public required string KeyId { get; init; }

    /// <summary>The key's lifecycle status at the time it was materialized.</summary>
    public required HuiaSigningKeyStatus Status { get; init; }

    /// <summary>A validation key (public). Always present.</summary>
    public required SecurityKey SecurityKey { get; init; }

    /// <summary>Signing credentials. Present only for keys whose private half was loaded (the active key).</summary>
    public SigningCredentials? SigningCredentials { get; init; }

    /// <summary>The public key as an <see cref="JsonWebKey"/>, for the tenant JWKS.</summary>
    public required JsonWebKey PublicJwk { get; init; }
}
