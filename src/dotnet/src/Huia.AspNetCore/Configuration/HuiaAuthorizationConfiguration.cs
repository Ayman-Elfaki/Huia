using Microsoft.Extensions.DependencyInjection;
using MR.AspNetCore.Pagination;
using OpenIddict.Validation.AspNetCore;

namespace Huia.AspNetCore.Configuration;

/// <summary>
/// Registers the one authorization policy Huia owns: <c>Huia:Api</c>, used by the token-protected
/// self-service surface. It pins the OpenIddict validation scheme so a call with no bearer token gets a
/// 401 challenge rather than falling through to the Identity login cookie (which <c>AddIdentity</c> makes
/// the default authenticate scheme).
/// </summary>
internal static class HuiaAuthorizationConfiguration
{
    public static IServiceCollection AddHuiaAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(HuiaConstants.Policies.Api, policy =>
            {
                policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });

        services.AddPagination();
        return services;
    }
}
