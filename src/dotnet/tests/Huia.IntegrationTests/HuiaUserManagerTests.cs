using Huia.AspNetCore.Identity;
using Huia.IntegrationTests.Infrastructure;

namespace Huia.IntegrationTests;

/// <summary>
/// Exercises the phone + external + classification helpers consolidated onto
/// <see cref="HuiaUserManager"/> against a real Identity + EF Core stack.
/// </summary>
public sealed class HuiaUserManagerTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() => _host = await HuiaTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Get_user_type_follows_password_then_external_then_phone()
    {
        var withPassword = await _host.SeedUserAsync("consumer", "pw@type.test", "Password1!");
        var withExternal = await _host.SeedUserAsync("consumer", "ext@type.test", password: null);
        await _host.AddExternalLoginAsync("consumer", withExternal, "HuiaExternal", "ext-key-1");
        // A record with BOTH a password and an external login is still a Password account.
        await _host.AddExternalLoginAsync("consumer", withPassword, "HuiaExternal", "ext-key-2");
        var phoneUserId = await _host.SeedPhoneUserAsync("phone", "+15005550101");
        var blankId = await _host.SeedUserAsync("consumer", "blank@type.test", password: null);

        var types = await _host.WithUserManagerAsync("consumer", async um => (
            Password: await um.GetUserTypeAsync((await um.FindByIdAsync(withPassword))!),
            External: await um.GetUserTypeAsync((await um.FindByIdAsync(withExternal))!),
            Blank: await um.GetUserTypeAsync((await um.FindByIdAsync(blankId))!)));
        var phoneType = await _host.WithUserManagerAsync("phone", async um =>
            await um.GetUserTypeAsync((await um.FindByIdAsync(phoneUserId))!));

        types.Password.ShouldBe(HuiaUserType.Password);
        types.External.ShouldBe(HuiaUserType.External);
        types.Blank.ShouldBe(HuiaUserType.Unknown);
        phoneType.ShouldBe(HuiaUserType.Phone);
    }

    [Fact]
    public async Task Find_by_phone_number_matches_the_phone_column_and_stays_tenant_scoped()
    {
        var id = await _host.SeedPhoneUserAsync("phone", "+15005550102");
        await _host.SeedPhoneUserAsync("phone-auto", "+15005550102"); // same number, other tenant

        var (found, missing) = await _host.WithUserManagerAsync("phone", async um => (
            Found: await um.FindByPhoneNumberAsync("+15005550102"),
            Missing: await um.FindByPhoneNumberAsync("+15005550999")));

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(id);
        missing.ShouldBeNull();
    }

    [Fact]
    public async Task Create_phone_user_makes_the_number_the_username_with_no_password_or_email()
    {
        var (result, user) = await _host.WithUserManagerAsync("phone", async um =>
            await um.CreatePhoneUserAsync("phone", "+15005550103", "Ada", "Lovelace"));

        result.Succeeded.ShouldBeTrue();

        var reloaded = await _host.WithUserManagerAsync("phone", async um => await um.FindByIdAsync(user.Id));
        reloaded!.UserName.ShouldBe("+15005550103");
        reloaded.PhoneNumber.ShouldBe("+15005550103");
        reloaded.PhoneNumberConfirmed.ShouldBeTrue();
        reloaded.Email.ShouldBeNull();
        (await _host.WithUserManagerAsync("phone", async um => await um.HasPasswordAsync(reloaded))).ShouldBeFalse();
    }

    [Fact]
    public async Task Create_external_user_creates_and_links_in_one_step()
    {
        var (withEmail, slugged) = await _host.WithUserManagerAsync("consumer", async um => (
            WithEmail: await um.CreateExternalUserAsync("consumer", "grace@ext.test", "Grace", "Hopper", "HuiaExternal", "gh-1", "Partner"),
            Slugged: await um.CreateExternalUserAsync("consumer", null, "No", "Email", "HuiaExternal", "ne-1", "Partner")));

        withEmail.Succeeded.ShouldBeTrue();
        withEmail.User.UserName.ShouldBe("grace@ext.test");
        slugged.User.UserName.ShouldContain("ne-1");

        var linked = await _host.WithUserManagerAsync("consumer", async um =>
            await um.FindByLoginAsync("HuiaExternal", "gh-1"));
        linked.ShouldNotBeNull();
        linked!.Id.ShouldBe(withEmail.User.Id);
    }

    [Fact]
    public async Task Try_link_external_by_email_gates_on_linking_confirmation_and_vouching()
    {
        await _host.SeedUserAsync("consumer", "match@link.test", "Password1!", emailConfirmed: true);
        await _host.SeedUserAsync("consumer", "unconfirmed@link.test", "Password1!", emailConfirmed: false);

        var outcomes = await _host.WithUserManagerAsync("consumer", async um => (
            NoMatch: await um.TryLinkExternalByEmailAsync("nobody@link.test", true, true, "HuiaExternal", "k0", "Partner"),
            LinkingOff: await um.TryLinkExternalByEmailAsync("match@link.test", true, false, "HuiaExternal", "k1", "Partner"),
            NotVouched: await um.TryLinkExternalByEmailAsync("match@link.test", false, true, "HuiaExternal", "k2", "Partner"),
            Unconfirmed: await um.TryLinkExternalByEmailAsync("unconfirmed@link.test", true, true, "HuiaExternal", "k3", "Partner"),
            Linked: await um.TryLinkExternalByEmailAsync("match@link.test", true, true, "HuiaExternal", "k4", "Partner")));

        outcomes.NoMatch.Outcome.ShouldBe(ExternalEmailLinkOutcome.NoMatch);
        outcomes.LinkingOff.Outcome.ShouldBe(ExternalEmailLinkOutcome.Blocked);
        outcomes.NotVouched.Outcome.ShouldBe(ExternalEmailLinkOutcome.Blocked);
        outcomes.Unconfirmed.Outcome.ShouldBe(ExternalEmailLinkOutcome.Blocked);
        outcomes.Linked.Outcome.ShouldBe(ExternalEmailLinkOutcome.Linked);
        outcomes.Linked.User.ShouldNotBeNull();
    }

    [Fact]
    public async Task Can_remove_external_login_only_when_another_sign_in_method_remains()
    {
        var loner = await _host.SeedUserAsync("consumer", "loner@rm.test", password: null);
        await _host.AddExternalLoginAsync("consumer", loner, "HuiaExternal", "loner-key");
        var withPassword = await _host.SeedUserAsync("consumer", "safe@rm.test", "Password1!");
        await _host.AddExternalLoginAsync("consumer", withPassword, "HuiaExternal", "safe-key");

        var (lonerCanRemove, safeCanRemove) = await _host.WithUserManagerAsync("consumer", async um => (
            Loner: await um.CanRemoveExternalLoginAsync((await um.FindByIdAsync(loner))!),
            Safe: await um.CanRemoveExternalLoginAsync((await um.FindByIdAsync(withPassword))!)));

        lonerCanRemove.ShouldBeFalse();
        safeCanRemove.ShouldBeTrue();
    }
}
