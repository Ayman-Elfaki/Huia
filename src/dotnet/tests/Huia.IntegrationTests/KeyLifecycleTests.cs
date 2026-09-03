using Huia.AspNetCore.Keys;
using Huia.EntityFrameworkCore;
using Huia.EntityFrameworkCore.Entities;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.IntegrationTests;

public sealed class KeyLifecycleTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(
            configureOptions: huia => huia.ConfigureKeys(keys =>
            {
                keys.EnableBackgroundJobs = false;
                keys.RotationInterval = TimeSpan.FromDays(30);
                keys.ActivationDelay = TimeSpan.FromHours(24);
                keys.RetentionPeriod = TimeSpan.FromDays(7);
                keys.RetiredKeyGracePeriod = TimeSpan.FromDays(7);
            }),
            timeProvider: _clock);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_key_moves_through_pending_active_rotated_retired_and_is_finally_deleted()
    {
        using var scope = _host.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<HuiaKeyLifecycleService>();
        var db = scope.ServiceProvider.GetRequiredService<HuiaDbContext>();
        var keyRing = scope.ServiceProvider.GetRequiredService<IHuiaKeyRing>();

        // The bootstrapper created an immediately-active key for 'acme' at start-up.
        var original = await db.SigningKeys.SingleAsync(k => k.TenantId == "acme");
        original.Status.ShouldBe(HuiaSigningKeyStatus.Active);

        // Age past the rotation interval -> a pending successor is created.
        _clock.Advance(TimeSpan.FromDays(30) + TimeSpan.FromHours(1));
        await lifecycle.RotateAsync(CancellationToken.None);
        var pending = await db.SigningKeys.SingleAsync(k => k.TenantId == "acme" && k.Status == HuiaSigningKeyStatus.Pending);

        // Reach the activation time -> pending becomes active, original becomes rotated (still published).
        _clock.Advance(TimeSpan.FromHours(25));
        await lifecycle.ActivateDueAsync(CancellationToken.None);

        await db.Entry(original).ReloadAsync();
        await db.Entry(pending).ReloadAsync();
        original.Status.ShouldBe(HuiaSigningKeyStatus.Rotated);
        pending.Status.ShouldBe(HuiaSigningKeyStatus.Active);

        await keyRing.InvalidateAsync("acme");
        var published = await keyRing.GetPublishedKeysAsync("acme");
        published.Select(k => k.KeyId).ShouldContain(original.KeyId);
        published.Select(k => k.KeyId).ShouldContain(pending.KeyId);

        // Past the retention period -> the rotated key is unpublished.
        _clock.Advance(TimeSpan.FromDays(8));
        await lifecycle.RetireAsync(CancellationToken.None);
        await db.Entry(original).ReloadAsync();
        original.Status.ShouldBe(HuiaSigningKeyStatus.Retired);

        // Past the grace period -> the retired key is deleted.
        _clock.Advance(TimeSpan.FromDays(8));
        await lifecycle.DeleteAsync(CancellationToken.None);
        (await db.SigningKeys.AnyAsync(k => k.KeyId == original.KeyId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Rotation_does_not_stack_pending_keys()
    {
        using var scope = _host.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<HuiaKeyLifecycleService>();
        var db = scope.ServiceProvider.GetRequiredService<HuiaDbContext>();

        _clock.Advance(TimeSpan.FromDays(40));
        await lifecycle.RotateAsync(CancellationToken.None);
        await lifecycle.RotateAsync(CancellationToken.None);

        (await db.SigningKeys.CountAsync(k => k.TenantId == "acme" && k.Status == HuiaSigningKeyStatus.Pending))
            .ShouldBe(1);
    }
}
