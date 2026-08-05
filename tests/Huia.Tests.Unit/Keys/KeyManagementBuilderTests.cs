using Huia.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using Quartz;

namespace Huia.Tests.Unit.Keys;

public class KeyManagementBuilderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (KeyManager Manager, FakeSigningKeyStore Store, ManualTimeProvider Time) CreateManager(
        KeyManagementOptions? options = null)
    {
        var time = new ManualTimeProvider(Start);
        var store = new FakeSigningKeyStore(time);

        var services = new ServiceCollection();
        services.AddSingleton<ISigningKeyStore>(store);
        var provider = services.BuildServiceProvider();

        var manager = new KeyManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options ?? new KeyManagementOptions(),
            time);

        return (manager, store, time);
    }


    [Fact]
    public void UseAutomaticKeyManagement_RegistersOptionsAndHostedService()
    {
        var services = new ServiceCollection();
        var builder = new KeyManagementBuilder(services);

        builder.UseAutomaticKeyManagement(options =>
        {
            options.RsaKeySizeInBits = 4096;
            options.SigningAlgorithm = SigningAlgorithm.PS256;
        });

        var options =
            services.Single(d => d.ServiceType == typeof(KeyManagementOptions)).ImplementationInstance as
                KeyManagementOptions;
        Assert.NotNull(options);
        Assert.Equal(4096, options.RsaKeySizeInBits);
        Assert.Equal(SigningAlgorithm.PS256, options.SigningAlgorithm);

        Assert.Contains(services,
            d => d.ServiceType == typeof(IHostedService) &&
                 d.ImplementationType == typeof(KeyManagementInitializer));
    }

    [Fact]
    public void UseAutomaticKeyManagement_RegistersKeyManagerAndServerOptionsWiring()
    {
        var services = new ServiceCollection();
        var builder = new KeyManagementBuilder(services);

        builder.UseAutomaticKeyManagement();

        Assert.Contains(services, d => d.ServiceType == typeof(KeyManager));
        Assert.Contains(services,
            d => d.ServiceType == typeof(IConfigureOptions<OpenIddictServerOptions>) &&
                 d.ImplementationType == typeof(ServerOptionsConfigurator));
        Assert.Contains(services,
            d => d.ServiceType == typeof(IOptionsChangeTokenSource<OpenIddictServerOptions>) &&
                 d.ImplementationType == typeof(KeyChangeTokenSource));
    }

    [Fact]
    public async Task UseAutomaticKeyManagement_SchedulesRotationJob_WithConfiguredCronExpression()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new KeyManagementBuilder(services);

        builder.UseAutomaticKeyManagement(options => options.RotationCronExpression = "0 30 4 * * ?");

        await using var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await schedulerFactory.GetScheduler();

        var jobKey = new JobKey("Huia.KeyRotation");
        Assert.True(await scheduler.CheckExists(jobKey));

        var triggers = await scheduler.GetTriggersOfJob(jobKey);
        var trigger = Assert.Single(triggers);
        var cronTrigger = Assert.IsType<ICronTrigger>(trigger, exactMatch: false);
        Assert.Equal("0 30 4 * * ?", cronTrigger.CronExpressionString);
    }

    [Fact]
    public void UseAutomaticKeyManagement_ComposesWithConfigureQuartzCallback()
    {
        var services = new ServiceCollection();
        var builder = new KeyManagementBuilder(services);
        var configureQuartzCalled = false;

        builder.UseAutomaticKeyManagement(configureQuartz: _ => configureQuartzCalled = true);

        Assert.True(configureQuartzCalled);
    }

    [Fact]
    public void UseManualKeyManagement_DoesNotRegisterQuartz()
    {
        var services = new ServiceCollection();
        var builder = new KeyManagementBuilder(services);

        builder.UseManualKeyManagement();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ISchedulerFactory));
    }

    [Fact]
    public void UseManualKeyManagement_RegistersManualInitializer_NotAutomatic()
    {
        var services = new ServiceCollection();
        var builder = new KeyManagementBuilder(services);

        builder.UseManualKeyManagement();

        Assert.Contains(services,
            d => d.ServiceType == typeof(IHostedService) &&
                 d.ImplementationType == typeof(ManualKeyManagementInitializer));
        Assert.DoesNotContain(services,
            d => d.ServiceType == typeof(IHostedService) &&
                 d.ImplementationType == typeof(KeyManagementInitializer));
    }

    [Fact]
    public void UseAutomaticKeyManagement_ReusesExistingOptionsInstance_FromPriorRegistration()
    {
        var services = new ServiceCollection();
        var preRegistered = new KeyManagementOptions { RsaKeySizeInBits = 3072 };
        services.AddSingleton(preRegistered);

        var builder = new KeyManagementBuilder(services);
        builder.UseAutomaticKeyManagement();

        var options =
            services.Single(d => d.ServiceType == typeof(KeyManagementOptions)).ImplementationInstance as
                KeyManagementOptions;
        Assert.Same(preRegistered, options);
        Assert.Equal(3072, options!.RsaKeySizeInBits);
    }


    [Fact]
    public async Task RotateAsync_CreatesAKeyForBothUsagesWhenNoneExist()
    {
        var (manager, store, _) = CreateManager();

        await manager.RotateAsync();

        Assert.Single(store.AllKeys, k => k.Usage == KeyUsage.Signing);
        Assert.Single(store.AllKeys, k => k.Usage == KeyUsage.Encryption);
    }

    [Fact]
    public async Task RotateAsync_PopulatesSnapshotWithTheCreatedKeys()
    {
        var (manager, _, _) = CreateManager();

        await manager.RotateAsync();

        Assert.Single(manager.Snapshot.SigningKeys);
        Assert.Single(manager.Snapshot.EncryptionKeys);
    }

    [Fact]
    public async Task RotateAsync_DoesNothingWhenTheCurrentKeyIsWellWithinItsActiveLifetime()
    {
        var options = new KeyManagementOptions
            { ActiveLifetime = TimeSpan.FromDays(90), RotationLeadTime = TimeSpan.FromDays(14) };
        var (manager, store, time) = CreateManager(options);

        await manager.RotateAsync();
        var keyCountAfterFirstRotation = store.AllKeys.Count;

        time.Advance(TimeSpan.FromDays(1));
        await manager.RotateAsync();

        Assert.Equal(keyCountAfterFirstRotation, store.AllKeys.Count);
    }

    [Fact]
    public async Task RotateAsync_GeneratesASuccessorOnceWithinTheRotationLeadTime()
    {
        var options = new KeyManagementOptions
            { ActiveLifetime = TimeSpan.FromDays(90), RotationLeadTime = TimeSpan.FromDays(14) };
        var (manager, store, time) = CreateManager(options);

        await manager.RotateAsync();

        // 90 - 14 = 76 days in: due for a successor, but the original key hasn't hit its own retirement (day
        // 90) yet, so the two are expected to briefly coexist, both non-retired, until day 90.
        time.Advance(TimeSpan.FromDays(76));
        await manager.RotateAsync();

        var signingKeys = store.AllKeys.Where(k => k.Usage == KeyUsage.Signing).ToList();
        Assert.Equal(2, signingKeys.Count);
        Assert.All(signingKeys, k => Assert.False(k.IsRetired));
    }

    [Fact]
    public async Task RotateAsync_TheNewestNonRetiredKeyIsFirstInTheSnapshot()
    {
        var options = new KeyManagementOptions
            { ActiveLifetime = TimeSpan.FromDays(90), RotationLeadTime = TimeSpan.FromDays(14) };
        var (manager, _, time) = CreateManager(options);

        await manager.RotateAsync();
        var firstKeyId = manager.Snapshot.SigningKeys[0].Id;

        time.Advance(TimeSpan.FromDays(76));
        await manager.RotateAsync();

        var current = manager.Snapshot.SigningKeys[0];
        Assert.NotEqual(firstKeyId, current.Id);
        Assert.False(current.IsRetired);
    }

    [Fact]
    public async Task RotateAsync_RetiresAKeyOnceItsActiveLifetimeHasFullyElapsed()
    {
        var options = new KeyManagementOptions
            { ActiveLifetime = TimeSpan.FromDays(90), RotationLeadTime = TimeSpan.FromDays(14) };
        var (manager, store, time) = CreateManager(options);

        await manager.RotateAsync();
        var originalKeyId = store.AllKeys.First(k => k.Usage == KeyUsage.Signing).Id;

        time.Advance(TimeSpan.FromDays(90));
        await manager.RotateAsync();

        var original = store.AllKeys.Single(k => k.Id == originalKeyId);
        Assert.True(original.IsRetired);
    }

    [Fact]
    public async Task RotateAsync_KeepsARetiredKeyValidUntilItsValidationOverlapExpires()
    {
        var options = new KeyManagementOptions
        {
            ActiveLifetime = TimeSpan.FromDays(90),
            RotationLeadTime = TimeSpan.FromDays(14),
            ValidationOverlap = TimeSpan.FromDays(30),
        };
        var (manager, store, time) = CreateManager(options);

        await manager.RotateAsync();
        var originalKeyId = store.AllKeys.First(k => k.Usage == KeyUsage.Signing).Id;

        time.Advance(TimeSpan.FromDays(90));
        await manager.RotateAsync();

        // Still within the 30-day validation overlap after retirement: GetValidKeysAsync should still return it.
        var validKeys = await store.GetValidKeysAsync(KeyUsage.Signing);
        Assert.Contains(validKeys, k => k.Id == originalKeyId);

        time.Advance(TimeSpan.FromDays(31));
        await manager.PurgeExpiredKeysAsync();

        Assert.DoesNotContain(store.AllKeys, k => k.Id == originalKeyId);
    }

    [Fact]
    public async Task RotateAsync_CalledRepeatedlyWithoutTimePassingIsIdempotent()
    {
        var (manager, store, _) = CreateManager();

        await manager.RotateAsync();
        await manager.RotateAsync();
        await manager.RotateAsync();

        Assert.Single(store.AllKeys, k => k.Usage == KeyUsage.Signing);
        Assert.Single(store.AllKeys, k => k.Usage == KeyUsage.Encryption);
    }

    [Fact]
    public async Task GetChangeToken_FiresAfterARotationThatChangesTheKeySet()
    {
        var options = new KeyManagementOptions
            { ActiveLifetime = TimeSpan.FromDays(90), RotationLeadTime = TimeSpan.FromDays(14) };
        var (manager, _, time) = CreateManager(options);

        await manager.RotateAsync();
        var token = manager.GetChangeToken();
        Assert.False(token.HasChanged);

        time.Advance(TimeSpan.FromDays(76));
        await manager.RotateAsync();

        Assert.True(token.HasChanged);
    }

    // Manual key management (UseManualKeyManagement): CreateKeyAsync/RetireKeyAsync/RefreshSnapshotAsync
    // bypass RotateAsync's lead-time/lifetime policy entirely — the caller decides when a key is created or
    // retired, rather than KeyManagementOptions.

    [Fact]
    public async Task CreateKeyAsync_CreatesExactlyOneKeyRegardlessOfExistingKeys()
    {
        var (manager, store, time) = CreateManager();

        await manager.CreateKeyAsync(KeyUsage.Signing, time.GetUtcNow().AddDays(90));
        await manager.CreateKeyAsync(KeyUsage.Signing, time.GetUtcNow().AddDays(90));

        // Unlike RotateAsync, CreateKeyAsync has no "already have a current key" check — it always creates.
        Assert.Equal(2, store.AllKeys.Count(k => k.Usage == KeyUsage.Signing));
    }

    [Fact]
    public async Task CreateKeyAsync_RefreshesTheSnapshot()
    {
        var (manager, _, time) = CreateManager();

        var created = await manager.CreateKeyAsync(KeyUsage.Signing, time.GetUtcNow().AddDays(90));

        Assert.Contains(manager.Snapshot.SigningKeys, k => k.Id == created.Id);
    }

    [Fact]
    public async Task RetireKeyAsync_MarksTheKeyRetiredAndRefreshesTheSnapshot()
    {
        var (manager, _, time) = CreateManager();
        var created = await manager.CreateKeyAsync(KeyUsage.Signing, time.GetUtcNow().AddDays(90));

        await manager.RetireKeyAsync(created.Id, time.GetUtcNow());

        Assert.True(Assert.Single(manager.Snapshot.SigningKeys, k => k.Id == created.Id).IsRetired);
    }

    [Fact]
    public async Task RefreshSnapshotAsync_PopulatesTheSnapshotWithoutCreatingOrRetiringAnything()
    {
        var (manager, store, time) = CreateManager();
        store.Seed(new KeyDescriptor
        {
            Id = "pre-existing",
            Usage = KeyUsage.Signing,
            CreatedAt = time.GetUtcNow(),
            ExpiresAt = time.GetUtcNow().AddDays(90),
            Pkcs8PrivateKey = [],
        });

        await manager.RefreshSnapshotAsync();

        Assert.Contains(manager.Snapshot.SigningKeys, k => k.Id == "pre-existing");
        Assert.Single(store.AllKeys);
    }
}