using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.Identity;

/// <summary>
/// A <see cref="HuiaUserManager"/> / <see cref="HuiaSignInManager"/> pair bound to one
/// <see cref="HuiaAuthFlow"/>, reading that flow's <see cref="IdentityOptions"/> instance. Obtained
/// from <see cref="IHuiaFlowIdentityFactory"/>.
/// </summary>
public sealed class HuiaFlowIdentity
{
    internal HuiaFlowIdentity(HuiaAuthFlow flow, IdentityOptions options, HuiaUserManager userManager, HuiaSignInManager signInManager)
    {
        Flow = flow;
        Options = options;
        UserManager = userManager;
        SignInManager = signInManager;
    }

    /// <summary>The flow this pair is bound to.</summary>
    public HuiaAuthFlow Flow { get; }

    /// <summary>The resolved <see cref="IdentityOptions"/> for this flow and the current tenant.</summary>
    public IdentityOptions Options { get; }

    /// <summary>A user manager whose <see cref="UserManager{TUser}.Options"/> is <see cref="Options"/>.</summary>
    public HuiaUserManager UserManager { get; }

    /// <summary>A sign-in manager whose options are <see cref="Options"/>.</summary>
    public HuiaSignInManager SignInManager { get; }
}

/// <summary>
/// Builds a <see cref="HuiaFlowIdentity"/> for a given <see cref="HuiaAuthFlow"/>. Scoped: the pair is
/// memoised per flow for the lifetime of the scope, and — like <see cref="HuiaUserManager"/> resolved
/// straight from DI — it binds to the tenant that is ambient <em>when it is first requested</em>, so
/// resolve it after tenant resolution or inside <c>HuiaTenantScope.Enter</c>.
/// </summary>
public interface IHuiaFlowIdentityFactory
{
    /// <summary>Gets (creating once per scope) the manager pair for <paramref name="flow"/>.</summary>
    /// <param name="flow">The authentication flow.</param>
    /// <returns>The flow's manager pair.</returns>
    HuiaFlowIdentity Create(HuiaAuthFlow flow);
}

/// <summary>Default <see cref="IHuiaFlowIdentityFactory"/>: constructs the managers by hand with the flow's named options.</summary>
internal sealed class HuiaFlowIdentityFactory(IServiceProvider services, IOptionsSnapshot<IdentityOptions> options)
    : IHuiaFlowIdentityFactory
{
    private readonly Dictionary<HuiaAuthFlow, HuiaFlowIdentity> _cache = [];

    public HuiaFlowIdentity Create(HuiaAuthFlow flow)
    {
        if (_cache.TryGetValue(flow, out var existing))
        {
            return existing;
        }

        var flowOptions = options.Get(HuiaFlowIdentityOptions.NameFor(flow));
        var accessor = new OptionsWrapper<IdentityOptions>(flowOptions);

        var userManager = new HuiaUserManager(
            services.GetRequiredService<IUserStore<HuiaUser>>(),
            accessor,
            services.GetRequiredService<IPasswordHasher<HuiaUser>>(),
            services.GetServices<IUserValidator<HuiaUser>>(),
            services.GetServices<IPasswordValidator<HuiaUser>>(),
            services.GetRequiredService<ILookupNormalizer>(),
            services.GetRequiredService<IdentityErrorDescriber>(),
            services,
            services.GetRequiredService<ILogger<UserManager<HuiaUser>>>());

        var signInManager = new HuiaSignInManager(
            userManager,
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IUserClaimsPrincipalFactory<HuiaUser>>(),
            accessor,
            services.GetRequiredService<ILogger<SignInManager<HuiaUser>>>(),
            services.GetRequiredService<IAuthenticationSchemeProvider>(),
            services.GetRequiredService<IUserConfirmation<HuiaUser>>());

        var flowIdentity = new HuiaFlowIdentity(flow, flowOptions, userManager, signInManager);
        _cache[flow] = flowIdentity;
        return flowIdentity;
    }
}
