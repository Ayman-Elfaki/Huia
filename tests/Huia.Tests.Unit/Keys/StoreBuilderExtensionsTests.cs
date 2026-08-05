using Huia.Core;
using Huia.Keys;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Unit.Keys;

public class StoreBuilderExtensionsTests
{
    [Fact]
    public void WithSigningKeyStore_RegistersTheKeyStoreAndOptions()
    {
        var services = new ServiceCollection();
        var builder = new HuiaBuilder(services);

        builder.WithSigningKeyStore<FakeSigningKeyStore>();

        Assert.Contains(services,
            d => d.ServiceType == typeof(ISigningKeyStore) && d.ImplementationType == typeof(FakeSigningKeyStore));
        Assert.Contains(services, d => d.ServiceType == typeof(KeyManagementOptions));
    }

    [Fact]
    public void WithSigningKeyStore_AppliesConfigureCallback()
    {
        var services = new ServiceCollection();
        var builder = new HuiaBuilder(services);

        builder.WithSigningKeyStore<FakeSigningKeyStore>(options => options.RsaKeySizeInBits = 4096);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<KeyManagementOptions>();
        Assert.Equal(4096, options.RsaKeySizeInBits);
    }

    [Fact]
    public void WithSigningKeyStore_CalledAfterAnExistingOptionsInstance_ReusesRatherThanOverwritesIt()
    {
        var services = new ServiceCollection();
        var builder = new HuiaBuilder(services);
        var existing = new KeyManagementOptions { RsaKeySizeInBits = 3072 };
        services.AddSingleton(existing);

        builder.WithSigningKeyStore<FakeSigningKeyStore>();

        var provider = services.BuildServiceProvider();
        Assert.Same(existing, provider.GetRequiredService<KeyManagementOptions>());
        Assert.Equal(3072, existing.RsaKeySizeInBits);
    }
}