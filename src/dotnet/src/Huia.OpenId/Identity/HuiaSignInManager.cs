using Huia.OpenId.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.OpenId.Identity;

/// <summary>The concrete Huia OpenId <see cref="Huia.Identity.HuiaSignInManager{TUser}"/>.</summary>
/// <remarks>Creates the manager. Parameters are the stock <see cref="SignInManager{TUser}"/> dependencies.</remarks>
public class HuiaSignInManager(
    UserManager<HuiaUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<HuiaUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<HuiaUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<HuiaUser> confirmation) :
    Huia.Identity.HuiaSignInManager<HuiaUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation);
