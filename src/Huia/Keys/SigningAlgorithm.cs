// ReSharper disable InconsistentNaming

namespace Huia.Keys;

/// <summary>
/// RSA signing algorithm used for newly-created signing keys, set via
/// <see cref="KeyManagementOptions.SigningAlgorithm"/>. All variants sign with the same RSA keypair
/// (<see cref="KeyManagementOptions.RsaKeySizeInBits"/> only), so changing this does not itself require
/// rotating existing keys; it takes effect for keys created from that point on, and each key's <c>alg</c> is
/// carried in its JWKS/JWT header, so retired keys keep validating under whichever algorithm they were
/// created with.
/// </summary>
public enum SigningAlgorithm
{
    /// <summary>
    /// RSASSA-PKCS1-v1_5 with SHA-256. OpenIddict's own default, and the most broadly interoperable.
    /// </summary>
    RS256,

    /// <summary>
    /// RSASSA-PKCS1-v1_5 with SHA-384.
    /// </summary>
    RS384,

    /// <summary>
    /// RSASSA-PKCS1-v1_5 with SHA-512.
    /// </summary>
    RS512,

    /// <summary>
    /// RSASSA-PSS with SHA-256 — the probabilistic scheme the OAuth 2.0 Security BCP recommends over PKCS1-v1_5 where interop allows it.
    /// </summary>
    PS256,

    /// <summary>
    /// RSASSA-PSS with SHA-384.
    /// </summary>
    PS384,

    /// <summary>
    /// RSASSA-PSS with SHA-512.
    /// </summary>
    PS512,
}