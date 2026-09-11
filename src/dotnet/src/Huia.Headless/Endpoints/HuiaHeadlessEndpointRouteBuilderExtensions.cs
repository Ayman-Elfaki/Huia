using Huia.Entities;
using Huia.Headless.Endpoints;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Routing;

/// <summary>Endpoint mapping for the single-tenant Headless flavor of Huia.</summary>
public static class HuiaHeadlessEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps ASP.NET Core Identity's own API endpoints (register, login, refresh, email confirmation,
    /// password reset, 2FA, <c>/manage/info</c>) under <c>identity/</c>, plus the Huia passkey endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHuiaHeadlessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGroup("identity").MapIdentityApi<HuiaUser>();
        endpoints.MapHuiaHeadlessPasskeyEndpoints();

        return endpoints;
    }
}
