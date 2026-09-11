using Microsoft.Extensions.DependencyInjection;
using MR.AspNetCore.Pagination;

namespace Huia.Configuration;

/// <summary>
/// Registers the authorization policy Huia owns: <c>Huia:Api</c>, used by the token-protected
/// self-service surface. It pins the policy scheme <see cref="HuiaConstants.Schemes.Api"/> so a call
/// with no bearer token gets a 401 challenge rather than falling through to an interactive scheme.
/// </summary>
internal static class HuiaAuthorizationConfiguration
{
    public static IServiceCollection AddHuiaAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(HuiaConstants.Policies.Api, policy =>
            {
                policy.AddAuthenticationSchemes(HuiaConstants.Schemes.Api);
                policy.RequireAuthenticatedUser();
            });

        services.AddPagination();
        return services;
    }
}
