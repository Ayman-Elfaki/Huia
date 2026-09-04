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
/// It is a straight pass-through today — the value is the seam: a phone sign-in reads options whose
/// <c>SignIn.RequireConfirmedEmail</c> is off while a password sign-in reads options whose
/// <c>SignIn.RequireConfirmedAccount</c> tracks the tenant, so <see cref="SignInManager{TUser}.CanSignInAsync"/>
/// is correct per flow with no bespoke branching.
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
    SignInManager<HuiaUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation);
