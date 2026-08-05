using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Huia.Keys;

/// <summary>
/// Feeds <see cref="KeyManager.Snapshot"/> into <c>OpenIddictServerOptions</c> every time options are
/// (re)built. Runs as an <c>IConfigureOptions</c>, so it always executes before OpenIddict's own
/// <c>IPostConfigureOptions&lt;OpenIddictServerOptions&gt;</c> — meaning the keys added here still go through
/// OpenIddict's normal validation/Kid-assignment/TokenValidationParameters wiring.
/// </summary>
internal sealed class ServerOptionsConfigurator(KeyManager keyManager, KeyManagementOptions options)
    : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions serverOptions)
    {
        var snapshot = keyManager.Snapshot;

        foreach (var key in snapshot.SigningKeys)
        {
            serverOptions.SigningCredentials.Add(CreateSigningCredentials(key));
        }

        foreach (var key in snapshot.EncryptionKeys)
        {
            serverOptions.EncryptionCredentials.Add(CreateEncryptingCredentials(key));
        }
    }

    private SigningCredentials CreateSigningCredentials(KeyDescriptor key)
    {
        var securityKey = CreateRsaSecurityKey(key);
        return new SigningCredentials(securityKey, ToJwtAlgorithm(options.SigningAlgorithm));
    }

    private static string ToJwtAlgorithm(SigningAlgorithm algorithm) => algorithm switch
    {
        SigningAlgorithm.RS256 => SecurityAlgorithms.RsaSha256,
        SigningAlgorithm.RS384 => SecurityAlgorithms.RsaSha384,
        SigningAlgorithm.RS512 => SecurityAlgorithms.RsaSha512,
        SigningAlgorithm.PS256 => SecurityAlgorithms.RsaSsaPssSha256,
        SigningAlgorithm.PS384 => SecurityAlgorithms.RsaSsaPssSha384,
        SigningAlgorithm.PS512 => SecurityAlgorithms.RsaSsaPssSha512,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, @"Unsupported signing algorithm."),
    };

    private static EncryptingCredentials CreateEncryptingCredentials(KeyDescriptor key)
    {
        var securityKey = CreateRsaSecurityKey(key);
        return new EncryptingCredentials(securityKey, SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512);
    }

    private static RsaSecurityKey CreateRsaSecurityKey(KeyDescriptor key)
    {
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(key.Pkcs8PrivateKey, out _);
        return new RsaSecurityKey(rsa) { KeyId = key.Id };
    }
}