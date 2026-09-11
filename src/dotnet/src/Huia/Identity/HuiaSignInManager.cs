using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.Identity;

/// <summary>
/// The Huia <see cref="SignInManager{TUser}"/>. Registered via <c>.AddSignInManager&lt;HuiaSignInManager&gt;()</c>
/// so every <see cref="SignInManager{TUser}"/> resolution gets it, and constructed directly by
/// <see cref="IHuiaFlowIdentityFactory"/> with a flow-specific <see cref="IdentityOptions"/> instance.
/// It is a thin seam over the framework: a phone sign-in reads options whose
/// <c>SignIn.RequireConfirmedEmail</c> is off while a password sign-in reads options whose
/// <c>SignIn.RequireConfirmedAccount</c> tracks the tenant, so <see cref="SignInManager{TUser}.CanSignInAsync"/>
/// is correct per flow with no bespoke branching. The one added member is the discoverable passkey sign-in.
/// </summary>
/// <remarks>Creates the manager. Parameters are the stock <see cref="SignInManager{TUser}"/> dependencies.</remarks>
public class HuiaSignInManager(
    UserManager<HuiaUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<HuiaUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<HuiaUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<HuiaUser> confirmation) :
    SignInManager<HuiaUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <summary>
    /// Completes a discoverable (usernameless) primary sign-in from a passkey assertion. Runs the
    /// standard pre-sign-in checks (lockout, the passkey flow's confirmation policy) and persists the
    /// advanced signature counter. The <c>amr</c> is a single <c>passkey</c>.
    /// </summary>
    /// <param name="credentialJson">The serialized WebAuthn assertion from the browser.</param>
    /// <param name="isPersistent">Whether to issue a persistent application cookie.</param>
    /// <returns>The sign-in result and, when the assertion verified, the account it resolved to.</returns>
    public async Task<(SignInResult Result, HuiaUser? User)> PasskeyPrimarySignInAsync(string credentialJson, bool isPersistent)
    {
        ArgumentException.ThrowIfNullOrEmpty(credentialJson);

        var assertion = await PerformPasskeyAssertionAsync(credentialJson);
        if (!assertion.Succeeded || assertion.User is not { } user || assertion.Passkey is not { } passkey)
        {
            return (SignInResult.Failed, null);
        }

        var preCheck = await PreSignInCheck(user);
        if (preCheck is not null)
        {
            return (preCheck, user);
        }

        // Persist the advanced signature counter / backup state, as PasskeySignInAsync would.
        if (!(await UserManager.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded)
        {
            return (SignInResult.Failed, null);
        }

        await SignInWithClaimsAsync(
            user, isPersistent,
            [new Claim(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.Passkey)]);
        return (SignInResult.Success, user);
    }
}
