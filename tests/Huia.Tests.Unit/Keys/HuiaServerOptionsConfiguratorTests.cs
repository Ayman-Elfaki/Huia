using System.Security.Cryptography;
using Huia.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Huia.Tests.Unit.Keys;

public class HuiaServerOptionsConfiguratorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SigningAlgorithm.RS256, SecurityAlgorithms.RsaSha256)]
    [InlineData(SigningAlgorithm.RS384, SecurityAlgorithms.RsaSha384)]
    [InlineData(SigningAlgorithm.RS512, SecurityAlgorithms.RsaSha512)]
    [InlineData(SigningAlgorithm.PS256, SecurityAlgorithms.RsaSsaPssSha256)]
    [InlineData(SigningAlgorithm.PS384, SecurityAlgorithms.RsaSsaPssSha384)]
    [InlineData(SigningAlgorithm.PS512, SecurityAlgorithms.RsaSsaPssSha512)]
    public async Task Configure_SigningCredentials_UseTheConfiguredAlgorithm(
        SigningAlgorithm algorithm, string expectedJwtAlgorithm)
    {
        var options = new KeyManagementOptions { SigningAlgorithm = algorithm };
        var (keyManager, _) = await CreateManagerWithASigningKeyAsync(options);

        var configurator = new ServerOptionsConfigurator(keyManager, options);
        var serverOptions = new OpenIddictServerOptions();
        configurator.Configure(serverOptions);

        var credentials = Assert.Single(serverOptions.SigningCredentials);
        Assert.Equal(expectedJwtAlgorithm, credentials.Algorithm);
    }

    /// <summary>
    /// The encryption key isn't affected by <see cref="SigningAlgorithm"/> — it
    /// always uses RSA-OAEP/A256CBC-HS512, regardless of what the signing key is configured to use.
    /// </summary>
    [Fact]
    public async Task Configure_EncryptionCredentials_AreUnaffectedByTheSigningAlgorithm()
    {
        var options = new KeyManagementOptions { SigningAlgorithm = SigningAlgorithm.PS512 };
        var time = new ManualTimeProvider(Start);
        var store = new FakeSigningKeyStore(time);
        store.Seed(CreateRealKeyDescriptor(KeyUsage.Encryption, time));

        var services = new ServiceCollection();
        services.AddSingleton<ISigningKeyStore>(store);
        var provider = services.BuildServiceProvider();
        var keyManager = new KeyManager(provider.GetRequiredService<IServiceScopeFactory>(), options, time);
        await keyManager.RefreshSnapshotAsync();

        var configurator = new ServerOptionsConfigurator(keyManager, options);
        var serverOptions = new OpenIddictServerOptions();
        configurator.Configure(serverOptions);

        var credentials = Assert.Single(serverOptions.EncryptionCredentials);
        Assert.Equal(SecurityAlgorithms.RsaOAEP, credentials.Alg);
        Assert.Equal(SecurityAlgorithms.Aes256CbcHmacSha512, credentials.Enc);
    }

    private static async Task<(KeyManager Manager, FakeSigningKeyStore Store)> CreateManagerWithASigningKeyAsync(
        KeyManagementOptions options)
    {
        var time = new ManualTimeProvider(Start);
        var store = new FakeSigningKeyStore(time);
        store.Seed(CreateRealKeyDescriptor(KeyUsage.Signing, time));

        var services = new ServiceCollection();
        services.AddSingleton<ISigningKeyStore>(store);
        var provider = services.BuildServiceProvider();

        var manager = new KeyManager(provider.GetRequiredService<IServiceScopeFactory>(), options, time);
        await manager.RefreshSnapshotAsync();

        return (manager, store);
    }

    /// <summary>
    /// Unlike <see cref="FakeSigningKeyStore.CreateKeyAsync"/> (which stores an empty <c>Pkcs8PrivateKey</c> —
    /// fine for the rotation-policy tests in <see cref="ServerOptionsConfigurator"/>, which never touch
    /// actual RSA material), <see cref="RSA.ImportPkcs8PrivateKey"/> imports the key via
    /// <see cref="RSA"/>, so it needs genuine key bytes.
    /// </summary>
    private static KeyDescriptor CreateRealKeyDescriptor(KeyUsage usage, TimeProvider time)
    {
        using var rsa = RSA.Create(2048);
        return new KeyDescriptor
        {
            Id = Guid.NewGuid().ToString("N"),
            Usage = usage,
            CreatedAt = time.GetUtcNow(),
            ExpiresAt = time.GetUtcNow().AddDays(90),
            Pkcs8PrivateKey = rsa.ExportPkcs8PrivateKey(),
        };
    }
}