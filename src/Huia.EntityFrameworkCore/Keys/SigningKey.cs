using Huia.Keys;

namespace Huia.EntityFrameworkCore.Keys;

/// <summary>
/// EF Core row backing one <see cref="KeyDescriptor"/>. Private key bytes are encrypted at rest.
/// </summary>
public class SigningKey
{
    /// <summary>
    /// Unique identifier for this key, used as the JWT/JWK <c>kid</c>.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Whether this key is used for signing or encrypting tokens.
    /// </summary>
    public KeyUsage Usage { get; set; }

    /// <summary>
    /// When this key was generated.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this key stops being valid even for validating previously issued tokens.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// When this key was retired from signing new tokens; <c>null</c> if still active.
    /// </summary>
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>
    /// PKCS#8 RSA private key, encrypted via ASP.NET Core Data Protection.
    /// </summary>
    public byte[] ProtectedPrivateKey { get; set; } = default!;
}