using Huia.Identity;
using Microsoft.AspNetCore.Identity;

namespace Huia.Common;

/// <summary>
/// Creates a new <see cref="HuiaUser"/> for a first-time external sign-in and links the external identity to
/// it — the two steps <c>ExternalLoginModel</c>'s conditional auto-provisioning and
/// <c>ExternalLoginConfirmationModel</c>'s own <c>OnPostAsync</c> both need, factored out so a bug in either
/// (e.g. forgetting to check <see cref="UserManager{TUser}.AddLoginAsync"/>'s result) only has one place to
/// happen. Deliberately stops at "account exists and the login is linked" — signing in, publishing events, and
/// the deferred-email-confirmation branch differ enough between the two callers (only the confirmation page
/// ever takes the latter, since auto-provisioning only runs when the provider already verified the email) that
/// folding them in here wouldn't actually remove complexity, just relocate it.
/// </summary>
internal static class ExternalAccountFactory
{
    /// <summary>
    /// Creates the account and links <paramref name="info"/> to it. Returns the created user, or <see
    /// langword="null"/> with <paramref name="errors"/> populated if either step failed.
    /// </summary>
    public static async Task<(HuiaUser? User, IReadOnlyList<string> Errors)> CreateAsync(
        UserManager<HuiaUser> userManager, ExternalLoginInfo info, string email, string firstName, string lastName,
        bool emailConfirmed)
    {
        var user = new StandardUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = emailConfirmed,
            Picture = ExternalClaimsMapper.GetPicture(info.Principal),
        };

        var createResult = await userManager.CreateAsync(user).ConfigureAwait(false);
        if (!createResult.Succeeded)
        {
            return (null, createResult.Errors.Select(e => e.Description).ToList());
        }

        var addLoginResult = await userManager.AddLoginAsync(user, info).ConfigureAwait(false);
        if (!addLoginResult.Succeeded)
        {
            return (null, addLoginResult.Errors.Select(e => e.Description).ToList());
        }

        return (user, []);
    }
}
