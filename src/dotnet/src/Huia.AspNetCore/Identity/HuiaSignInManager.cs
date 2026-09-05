using System.Security.Claims;
using Huia.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.AspNetCore.Identity;

/// <summary>
/// The Huia <see cref="SignInManager{TUser}"/>. Registered via <c>.AddSignInManager&lt;HuiaSignInManager&gt;()</c>
/// so every <see cref="SignInManager{TUser}"/> resolution gets it, and constructed directly by
/// <see cref="IHuiaFlowIdentityFactory"/> with a flow-specific <see cref="IdentityOptions"/> instance.
/// It is a thin seam over the framework: a phone sign-in reads options whose
/// <c>SignIn.RequireConfirmedEmail</c> is off while a password sign-in reads options whose
/// <c>SignIn.RequireConfirmedAccount</c> tracks the tenant, so <see cref="SignInManager{TUser}.CanSignInAsync"/>
/// is correct per flow with no bespoke branching. The one added member is the passkey step-up.
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
    /// Completes a discoverable (usernameless) primary sign-in from a passkey assertion. A passkey is a
    /// multi-factor credential in its own right — possession plus user verification — so this does
    /// <em>not</em> route through the two-factor branch even when the account has a second factor
    /// enabled; it still runs the standard pre-sign-in checks (lockout, confirmation policy for the
    /// passkey flow). The <c>amr</c> is a single <c>passkey</c>.
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

    /// <summary>
    /// Completes a second-factor sign-in with a passkey assertion. The primary (password) step already
    /// ran; <paramref name="expectedUserId"/> is the account it authenticated, carried in the protected
    /// flow token rather than the transient cookie (the passkey ceremony overwrites that cookie). On
    /// success the full application cookie is issued with an <c>amr</c> of password + passkey + mfa and,
    /// when asked, the browser is remembered so the second factor is skipped next time.
    /// </summary>
    /// <param name="expectedUserId">The id of the account the password step authenticated.</param>
    /// <param name="credentialJson">The serialized WebAuthn assertion from the browser.</param>
    /// <param name="isPersistent">Whether to issue a persistent application cookie.</param>
    /// <param name="rememberClient">Whether to drop a "remember this device" cookie.</param>
    /// <returns><see cref="SignInResult.Success"/> when the assertion verifies for the expected user.</returns>
    public async Task<SignInResult> PasskeyStepUpSignInAsync(
        string expectedUserId, string credentialJson, bool isPersistent, bool rememberClient)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedUserId);
        ArgumentException.ThrowIfNullOrEmpty(credentialJson);

        var assertion = await PerformPasskeyAssertionAsync(credentialJson);
        if (!assertion.Succeeded || assertion.User is not { } user || assertion.Passkey is not { } passkey
            || !string.Equals(user.Id, expectedUserId, StringComparison.Ordinal))
        {
            return SignInResult.Failed;
        }

        var preCheck = await PreSignInCheck(user);
        if (preCheck is not null)
        {
            return preCheck;
        }

        if (!(await UserManager.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded)
        {
            return SignInResult.Failed;
        }

        await Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

        Claim[] amr =
        [
            new(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.Password),
            new(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.Passkey),
            new(HuiaConstants.ClaimTypes.AuthenticationMethod, HuiaConstants.AuthenticationMethods.MultiFactor),
        ];
        await SignInWithClaimsAsync(user, isPersistent, amr);

        if (rememberClient)
        {
            await RememberTwoFactorClientAsync(user);
        }

        return SignInResult.Success;
    }
}
