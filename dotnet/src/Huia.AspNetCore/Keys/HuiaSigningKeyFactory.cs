using System.Security.Cryptography;
using System.Text.Json;
using Huia.EntityFrameworkCore.Entities;
using Huia.Options;
using Microsoft.IdentityModel.Tokens;

namespace Huia.AspNetCore.Keys;

/// <summary>Generates fresh RSA signing keys and renders their public half as a JWK.</summary>
internal sealed class HuiaSigningKeyFactory(IHuiaKeyProtector protector, TimeProvider timeProvider)
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

        using var rsa = RSA.Create(options.KeySize);
        var keyId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));
        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        var publicJwk = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["kid"] = keyId,
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
        };

        return new HuiaSigningKey
        {
            TenantId = tenantId,
            KeyId = keyId,
            Algorithm = SecurityAlgorithms.RsaSha256,
            PublicJwkJson = JsonSerializer.Serialize(publicJwk),
            WrappedPrivateKey = protector.Protect(rsa.ExportPkcs8PrivateKey()),
            Status = status,
            CreatedAt = timeProvider.GetUtcNow(),
            ActivateAt = activateAt,
        };
    }
}
