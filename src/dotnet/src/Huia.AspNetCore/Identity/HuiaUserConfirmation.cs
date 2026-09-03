using Huia.EntityFrameworkCore.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Identity;

namespace Huia.AspNetCore.Identity;

/// <summary>
/// Flow-aware account confirmation. This is consulted by <c>SignInManager.CanSignInAsync</c> on the
/// interactive password path: it requires a confirmed email when the tenant asks for one. The
/// passwordless SMS path signs in with <c>SignInWithClaimsAsync</c>, which never calls this — there the
/// confirmed phone number is the gate, enforced in the OTP flow itself.
/// </summary>
internal sealed class HuiaUserConfirmation(IMultiTenantContextAccessor tenantAccessor, HuiaOptions options)
    : IUserConfirmation<HuiaUser>
{
    public async Task<bool> IsConfirmedAsync(UserManager<HuiaUser> manager, HuiaUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        var tenantId = tenantAccessor.CurrentTenantId();
        if (tenantId is null || !options.Tenants.TryGetValue(tenantId, out var tenant))
        {
            return true;
        }

        if (!tenant.Authentication.Password.RequireConfirmedEmail)
        {
            return true;
        }

        return await manager.IsEmailConfirmedAsync(user);
    }
}
