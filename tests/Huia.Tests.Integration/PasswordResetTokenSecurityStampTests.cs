using Huia.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// Confirms <c>DataProtectorHuiaTokenProvider</c>'s password-reset tokens are bound to
/// <see cref="HuiaUser.SecurityStamp"/> — the same invariant ASP.NET Core Identity's own
/// <c>DataProtectorTokenProvider</c> guaranteed: a reset token issued before a password change must stop
/// validating once that change bumps the stamp, even though the token itself hasn't expired and was never
/// explicitly revoked. Regression coverage for that binding surviving the Identity-to-Huia rewrite.
/// </summary>
public class PasswordResetTokenSecurityStampTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string OriginalPassword = "P@ssw0rd123!";
    private const string ChangedPassword = "NewP@ssw0rd456!";
    private const string AttemptedPassword = "AnotherP@ssw0rd789!";

    [Fact]
    public async Task ResetPasswordAsync_WithTokenIssuedBeforePasswordChange_IsRejectedAfterTheChange()
    {
        var client = factory.CreateClient();
        var email = $"reset-stamp-test-{Guid.NewGuid():N}@example.com";
        await IdentityUiTestHelpers.RegisterAsync(client, email, OriginalPassword);

        string resetToken;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByEmailAsync(email)
                       ?? throw new InvalidOperationException("Registered user not found.");
            resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        }

        // The token, used right away, must still validate - proves the failure below is really about the
        // subsequent stamp change, not some other defect in the token itself. HuiaUserManager doesn't expose a
        // non-consuming "just validate" call for the reset-password purpose, so this goes straight to the
        // provider it delegates to.
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            var provider = scope.ServiceProvider.GetServices<IHuiaTokenProvider>()
                .Single(p => p.Name == HuiaTokenProviders.Default);
            var user = await userManager.FindByEmailAsync(email)
                       ?? throw new InvalidOperationException("Registered user not found.");

            var valid = await provider.ValidateAsync(user, "ResetPassword", resetToken, CancellationToken.None);
            Assert.True(valid);
        }

        // Changing the password bumps SecurityStamp (HuiaUserManager.ChangePasswordAsync) - the same event
        // that would invalidate an outstanding session cookie via the periodic security-stamp revalidation.
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByEmailAsync(email)
                       ?? throw new InvalidOperationException("Registered user not found.");
            var changeResult = await userManager.ChangePasswordAsync(user, OriginalPassword, ChangedPassword);
            Assert.True(changeResult.Succeeded);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
            var user = await userManager.FindByEmailAsync(email)
                       ?? throw new InvalidOperationException("Registered user not found.");

            var result = await userManager.ResetPasswordAsync(user, resetToken, AttemptedPassword);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => string.Equals(e.Code, "InvalidToken", StringComparison.Ordinal));
        }
    }
}
