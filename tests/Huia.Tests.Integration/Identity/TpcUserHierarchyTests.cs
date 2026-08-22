using Huia.Identity;
using Huia.Stores;
using Huia.TodoApi.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration.Identity;

/// <summary>
/// Covers Huia's table-per-concrete-type (TPC) user hierarchy itself — <see cref="PhoneUser"/> and
/// <see cref="StandardUser"/> mapped to their own tables (<c>HuiaPhoneUsers</c>/<c>HuiaUsers</c>) under the
/// abstract <see cref="HuiaUser"/> root — rather than any specific sign-in flow. See
/// <c>ModelBuilderExtensions.UseHuiaTpcUserHierarchy</c> for the mapping itself and why
/// <c>HuiaUserClaims</c>/<c>HuiaUserLogins</c>/<c>HuiaUserTokens</c>/<c>HuiaUserRoles</c> no longer have a
/// database-level foreign key into either user table.
/// </summary>
public class TpcUserHierarchyTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    [Fact]
    public async Task UserManager_FindByIdAsync_ResolvesCorrectLeafType_ForBothTables()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();

        var phoneUser = await CreatePhoneUserAsync(userManager);
        var standardUser = await CreateStandardUserAsync(userManager);

        var foundPhoneUser = await userManager.FindByIdAsync(phoneUser.Id);
        var foundStandardUser = await userManager.FindByIdAsync(standardUser.Id);

        Assert.IsType<PhoneUser>(foundPhoneUser);
        Assert.IsType<StandardUser>(foundStandardUser);
    }

    [Fact]
    public async Task UserManagerUsers_QueriesAcrossBothLeafTables_ViaTheAbstractBase()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();

        var phoneUser = await CreatePhoneUserAsync(userManager);
        var standardUser = await CreateStandardUserAsync(userManager);

        // UserManager<HuiaUser>.Users is DbSet<HuiaUser> under the hood — under TPC this is a UNION ALL across
        // HuiaPhoneUsers and HuiaUsers, materializing each row as its correct concrete leaf type.
        var ids = new[] { phoneUser.Id, standardUser.Id };
        var found = userManager.Users.Where(u => ids.Contains(u.Id)).ToList();

        Assert.Equal(2, found.Count);
        Assert.Contains(found, u => u is PhoneUser);
        Assert.Contains(found, u => u is StandardUser);
    }

    [Fact]
    public async Task PhoneNumberStore_FindsEitherLeafType_ByNormalizedPhoneNumber()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        var phoneNumberStore = scope.ServiceProvider.GetRequiredService<IHuiaPhoneNumberStore>();

        var phoneNumber = NewPhoneNumber();
        var phoneUser = await CreatePhoneUserAsync(userManager, phoneNumber);

        // A StandardUser can also record a phone number (account-management/future-2FA use) without it
        // becoming a sign-in credential — that's exactly what distinguishes it from PhoneUser. Give it a
        // different number so both lookups below are unambiguous.
        var standardPhoneNumber = NewPhoneNumber();
        var standardUser = await CreateStandardUserAsync(userManager);
        standardUser.PhoneNumber = standardPhoneNumber;
        standardUser.NormalizedPhoneNumber = standardPhoneNumber;
        await userManager.UpdateAsync(standardUser);

        var foundForPhoneUser = await phoneNumberStore.FindByNormalizedPhoneNumberAsync(phoneNumber, CancellationToken.None);
        var foundForStandardUser =
            await phoneNumberStore.FindByNormalizedPhoneNumberAsync(standardPhoneNumber, CancellationToken.None);

        Assert.IsType<PhoneUser>(foundForPhoneUser);
        Assert.Equal(phoneUser.Id, foundForPhoneUser!.Id);
        Assert.IsType<StandardUser>(foundForStandardUser);
        Assert.Equal(standardUser.Id, foundForStandardUser!.Id);
    }

    /// <summary>
    /// TPC removes the database-level foreign key (and therefore <c>ON DELETE CASCADE</c>) from
    /// <c>HuiaUserClaims</c>/<c>HuiaUserLogins</c>/<c>HuiaUserTokens</c>/<c>HuiaUserRoles</c> into either user
    /// table — <c>EfCoreHuiaUserStore.DeleteAsync</c> is what now cleans those rows up explicitly. This is the
    /// regression that mitigation exists to prevent: without it, a deleted user's dependent rows would be
    /// silently orphaned instead of removed.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_RemovesDependentRows_DespiteNoDatabaseCascade()
    {
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<HuiaRole>>();

            var user = await CreateStandardUserAsync(userManager);
            userId = user.Id;

            await userManager.AddClaimsAsync(user,
                [new System.Security.Claims.Claim("test-claim", "test-value")]);
            await userManager.AddLoginAsync(user, new UserLoginInfo("test-provider", "test-key", "Test Provider"));
            await userManager.SetAuthenticationTokenAsync(user, "test-provider", "test-token", "test-value");

            const string role = "TpcDeleteTestRole";
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new HuiaRole { Name = role });
            }

            await userManager.AddToRoleAsync(user, role);

            // Sanity check the dependent rows actually landed before deleting, so a later "zero rows" assertion
            // can't pass for the trivial reason that nothing was ever created.
            var db = scope.ServiceProvider.GetRequiredService<HuiaAppDbContext>();
            Assert.Equal(1, await db.Set<IdentityUserClaim<string>>().CountAsync(c => c.UserId == userId));
            Assert.Equal(1, await db.Set<IdentityUserLogin<string>>().CountAsync(l => l.UserId == userId));
            Assert.Equal(1, await db.Set<IdentityUserToken<string>>().CountAsync(t => t.UserId == userId));
            Assert.Equal(1, await db.Set<IdentityUserRole<string>>().CountAsync(r => r.UserId == userId));

            var result = await userManager.DeleteAsync(user);
            Assert.True(result.Succeeded);
        }

        // A fresh scope/context, not the one the delete ran through, so this can't pass merely because of
        // local, in-memory change tracking that doesn't reflect what's actually in the database.
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<HuiaAppDbContext>();

        Assert.Equal(0, await verifyDb.Set<IdentityUserClaim<string>>().CountAsync(c => c.UserId == userId));
        Assert.Equal(0, await verifyDb.Set<IdentityUserLogin<string>>().CountAsync(l => l.UserId == userId));
        Assert.Equal(0, await verifyDb.Set<IdentityUserToken<string>>().CountAsync(t => t.UserId == userId));
        Assert.Equal(0, await verifyDb.Set<IdentityUserRole<string>>().CountAsync(r => r.UserId == userId));
        Assert.Null(await verifyDb.Users.FirstOrDefaultAsync(u => u.Id == userId));
    }

    private static int _counter;

    private static async Task<PhoneUser> CreatePhoneUserAsync(UserManager<HuiaUser> userManager, string? phoneNumber = null)
    {
        phoneNumber ??= NewPhoneNumber();
        var user = new PhoneUser
        {
            UserName = phoneNumber,
            PhoneNumber = phoneNumber,
            NormalizedPhoneNumber = phoneNumber,
            PhoneNumberConfirmed = true,
        };

        var result = await userManager.CreateAsync(user);
        Assert.True(result.Succeeded);
        return user;
    }

    private static async Task<StandardUser> CreateStandardUserAsync(UserManager<HuiaUser> userManager)
    {
        var email = NewEmail();
        var user = new StandardUser { UserName = email, Email = email, EmailConfirmed = true };

        var result = await userManager.CreateAsync(user, "P@ssw0rd123!");
        Assert.True(result.Succeeded);
        return user;
    }

    // A valid, distinct-per-call NANP number (san-francisco/415 area code, 555 exchange — the vast majority of
    // the 555 exchange is a genuinely valid, assignable number; only 555-0100 through 555-0199 are reserved
    // fictional numbers, so starting the suffix at 0200 avoids that block).
    private static string NewPhoneNumber()
    {
        var n = Interlocked.Increment(ref _counter);
        var suffix = 200 + n % 9700;
        return $"+1415555{suffix:D4}";
    }

    private static string NewEmail() => $"tpc-hierarchy-test-{Guid.NewGuid():N}@example.com";
}
