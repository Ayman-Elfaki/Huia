using System.Security.Cryptography;
using System.Text.Json;
using Huia.OpenId.Entities;
using Huia.Options;
using Microsoft.IdentityModel.Tokens;

namespace Huia.OpenId.Keys;

/// <summary>Generates fresh RSA signing keys and renders their public half as a JWK.</summary>
internal sealed class HuiaSigningKeyFactory(
    IHuiaKeyProtector protector,
    TimeProvider timeProvider,
    Func<int, (byte[] Modulus, byte[] Exponent, byte[] Pkcs8PrivateKey)>? keyMaterialProvider = null)
{
    /// <summary>Creates a new key for a tenant. The caller decides the initial status and activation time.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="options">Key-management options (drives the RSA size).</param>
    /// <param name="status">The initial lifecycle status.</param>
    /// <param name="activateAt">When the key becomes eligible to sign.</param>
    /// <returns>The persistable entity.</returns>
    public HuiaSigningKey Create(string tenantId, KeyManagementOptions options, HuiaSigningKeyStatus status, DateTimeOffset activateAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(options);

        byte[] modulus;
        byte[] exponent;
        byte[] pkcs8PrivateKey;

        if (keyMaterialProvider is not null)
        {
            (modulus, exponent, pkcs8PrivateKey) = keyMaterialProvider(options.KeySize);
        }
        else
        {
            using var rsa = RSA.Create(options.KeySize);
            var parameters = rsa.ExportParameters(includePrivateParameters: false);
            modulus = parameters.Modulus!;
            exponent = parameters.Exponent!;
            pkcs8PrivateKey = rsa.ExportPkcs8PrivateKey();
        }

        var keyId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));

        var publicJwk = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["kid"] = keyId,
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["n"] = Base64UrlEncoder.Encode(modulus),
            ["e"] = Base64UrlEncoder.Encode(exponent),
        };

        return new HuiaSigningKey
        {
            TenantId = tenantId,
            KeyId = keyId,
            Algorithm = SecurityAlgorithms.RsaSha256,
            PublicJwkJson = JsonSerializer.Serialize(publicJwk),
            WrappedPrivateKey = protector.Protect(pkcs8PrivateKey),
            Status = status,
            CreatedAt = timeProvider.GetUtcNow(),
            ActivateAt = activateAt,
        };
    }
}
