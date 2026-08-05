using Huia.Sessions;

namespace Huia.Tests.Unit.Sessions;

public class UserSessionDescriptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static UserSessionDescriptor Create(DateTimeOffset expiresAt, DateTimeOffset? revokedAt = null) => new()
    {
        Id = "session-1",
        UserId = "user-1",
        CreatedAt = Now.AddMinutes(-10),
        LastActivityAt = Now.AddMinutes(-1),
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
    };

    [Fact]
    public void IsLive_NotRevoked_NotExpired_ReturnsTrue() =>
        Assert.True(Create(Now.AddMinutes(1)).IsLive(Now));

    [Fact]
    public void IsLive_PastExpiry_ReturnsFalse() =>
        Assert.False(Create(Now.AddMinutes(-1)).IsLive(Now));

    [Fact]
    public void IsLive_Revoked_ReturnsFalse_EvenIfNotYetExpired() =>
        Assert.False(Create(Now.AddMinutes(1), revokedAt: Now.AddMinutes(-1)).IsLive(Now));
}