using Huia.Headless.Services;
using Shouldly;

namespace Huia.Tests;

public sealed class HeadlessStoresUnitTests
{
    private sealed class TestTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    [Fact]
    public void PhoneLoginFlowStore_creates_retrieves_and_verifies_flow()
    {
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var store = new PhoneLoginFlowStore(time);

        var id = store.Create("+15551234567", "user-123", null);
        id.ShouldNotBeNullOrWhiteSpace();

        var flow = store.Get(id);
        flow.ShouldNotBeNull();
        flow.Id.ShouldBe(id);
        flow.PhoneNumber.ShouldBe("+15551234567");
        flow.UserId.ShouldBe("user-123");
        flow.PendingSignupId.ShouldBeNull();
        flow.Verified.ShouldBeFalse();

        store.MarkVerified(id);
        var verifiedFlow = store.Get(id);
        verifiedFlow.ShouldNotBeNull();
        verifiedFlow.Verified.ShouldBeTrue();

        store.Remove(id);
        store.Get(id).ShouldBeNull();
    }

    [Fact]
    public void PhoneLoginFlowStore_expires_after_ttl()
    {
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var store = new PhoneLoginFlowStore(time);

        var id = store.Create("+15559876543", null, "signup-456");
        store.Get(id).ShouldNotBeNull();

        // Advance past 10 min lifetime
        time.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));

        store.Get(id).ShouldBeNull();
    }

    [Fact]
    public void ExternalLoginFlowStore_creates_and_retrieves_user_flow()
    {
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var store = new ExternalLoginFlowStore(time);

        var code = store.CreateForUser("user-789", "google");
        code.ShouldNotBeNullOrWhiteSpace();

        var flow = store.Get(code);
        flow.ShouldNotBeNull();
        flow.Code.ShouldBe(code);
        flow.UserId.ShouldBe("user-789");
        flow.LoginProvider.ShouldBe("google");
        flow.PendingSignup.ShouldBeNull();

        store.Remove(code);
        store.Get(code).ShouldBeNull();
    }

    [Fact]
    public void ExternalLoginFlowStore_creates_and_retrieves_signup_flow()
    {
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var store = new ExternalLoginFlowStore(time);

        var signup = new ExternalSignup("github", "gh-123", "GitHub", "octo@cat.test", "Mona", "Lisa");
        var code = store.CreateForSignup(signup);

        var flow = store.Get(code);
        flow.ShouldNotBeNull();
        flow.PendingSignup.ShouldNotBeNull();
        flow.PendingSignup.LoginProvider.ShouldBe("github");
        flow.PendingSignup.Email.ShouldBe("octo@cat.test");
        flow.PendingSignup.FirstName.ShouldBe("Mona");
        flow.PendingSignup.LastName.ShouldBe("Lisa");

        // Expire after 10 min
        time.Advance(TimeSpan.FromMinutes(11));
        store.Get(code).ShouldBeNull();
    }
}
