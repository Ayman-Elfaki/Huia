using System.Security.Claims;
using Huia.Eventing;
using Huia.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.Identity;


/// <summary>
/// Extends <see cref="SignInManager{TUser}"/> so every sign-in creates or reuses a Huia session as part of
/// the same call, instead of the old two-step "sign in, then re-sign-in with a sid claim" pattern (the
/// now-removed <c>SessionSignInHelper</c>), which silently dropped any claims the first sign-in call added
/// (e.g. the <c>amr</c> claim <see cref="SignInManager{TUser}.TwoFactorAuthenticatorSignInAsync"/> adds)
/// since the second sign-in rebuilt the principal from scratch. Registered in place of the stock
/// <c>SignInManager&lt;HuiaUser&gt;</c> via <c>.AddSignInManager&lt;HuiaSignInManager&gt;()</c> — see
/// <c>ServiceCollectionExtensions</c>.
/// </summary>
public sealed class HuiaSignInManager(
    UserManager<HuiaUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<HuiaUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<HuiaUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<HuiaUser> confirmation,
    UserSessionService sessions)
    : SignInManager<HuiaUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <summary>
    /// The session id stamped onto the most recently created/reused principal in this request — read this
    /// right after a successful <c>PasswordSignInAsync</c>/<c>TwoFactorAuthenticatorSignInAsync</c>/
    /// <c>SignInAsync</c> call, e.g. to include it in a <see cref="UserSignedInEvent"/>. Null until the first
    /// sign-in this request.
    /// </summary>
    public string? CurrentSessionId { get; private set; }

    /// <summary>
    /// The one shared building block every high-level sign-in method (<c>PasswordSignInAsync</c>,
    /// <c>SignInAsync</c>, <c>SignInWithClaimsAsync</c>, and — importantly — <c>RefreshSignInAsync</c>) funnels
    /// through. Reuses the current request's own live session (keyed by matching the <c>sid</c> claim already
    /// on <see cref="SignInManager{TUser}.Context"/>'s principal against this same user) rather than always
    /// minting a new one — otherwise a claims-only refresh via <c>RefreshSignInAsync</c> (not a real sign-in)
    /// would spawn a fresh session every time it's called.
    /// </summary>
    public override async Task<ClaimsPrincipal> CreateUserPrincipalAsync(HuiaUser user)
    {
        var principal = await base.CreateUserPrincipalAsync(user).ConfigureAwait(false);
        var userId = await UserManager.GetUserIdAsync(user).ConfigureAwait(false);

        var existingSessionId = Context.User.FindFirstValue(SessionClaimTypes.Sid);
        var existingUserId = Context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (existingSessionId is not null && existingUserId == userId
            && await sessions.IsLiveAsync(existingSessionId).ConfigureAwait(false))
        {
            CurrentSessionId = existingSessionId;
            await sessions.TouchAsync(CurrentSessionId).ConfigureAwait(false);
        }
        else
        {
            CurrentSessionId = await sessions.CreateAsync(userId, Context).ConfigureAwait(false);
        }

        principal.Identities.First().AddClaim(new Claim(SessionClaimTypes.Sid, CurrentSessionId));
        return principal;
    }
}
