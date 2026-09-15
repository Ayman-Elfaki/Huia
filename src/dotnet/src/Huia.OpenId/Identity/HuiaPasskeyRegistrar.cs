using Huia.Events;
using Huia.Multitenancy;
using Huia.OpenId.EntityFrameworkCore.Entities;

namespace Huia.OpenId.Identity;

/// <summary>The concrete Huia OpenId <see cref="Huia.Identity.HuiaPasskeyRegistrar{TUser}"/>.</summary>
public sealed class HuiaPasskeyRegistrar(
    HuiaSignInManager signInManager,
    HuiaUserManager userManager,
    IHuiaTenantContext tenantContext,
    IHuiaEventPublisher events,
    TimeProvider timeProvider) :
    Huia.Identity.HuiaPasskeyRegistrar<HuiaUser>(signInManager, userManager, tenantContext, events, timeProvider);
