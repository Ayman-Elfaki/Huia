using Huia.Endpoints.Admin;
using Huia.Endpoints.Connect;
using Huia.Endpoints.Manage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Validation.AspNetCore;

namespace Huia.Endpoints;

/// <summary>
/// Maps Huia's OpenIddict, account-management, and admin-API endpoints onto an <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps Huia's OpenIddict connect endpoints under <c>/connect</c>: <c>authorize</c>, <c>token</c>,
        /// <c>userinfo</c>, <c>logout</c>, <c>device</c> (handled by OpenIddict itself), and <c>verify</c>.
        /// Returns the <c>connect</c> <see cref="RouteGroupBuilder"/> so callers can layer extra conventions
        /// (rate limiting, CORS, etc.) onto every connect endpoint at once.
        /// </summary>
        public RouteGroupBuilder MapHuiaConnectEndpoints()
        {
            var group = endpoints.MapGroup("connect");

            group.MapAuthorizationEndpoints();
            group.MapDeviceEndpoints();
            group.MapTokenEndpoints();
            group.MapUserinfoEndpoints();
            group.MapEndSessionEndpoints();

            return group;
        }

        /// <summary>
        /// Maps the account-management JSON endpoints a signed-in user calls to manage their own account:
        /// <c>/api/identity/manage/2fa</c>, <c>/api/identity/manage/info</c>,
        /// <c>/api/identity/manage/external-logins</c> — for an SPA/native/server-side OAuth client to build
        /// its own account-settings UI against. Accepts either the <c>Identity.Application</c> cookie
        /// (same-origin server-rendered callers) or a bearer access token validated by OpenIddict
        /// (cross-origin OAuth clients, the same scheme <c>userinfo</c> uses) — except
        /// <c>external-logins/{provider}/link(-callback)</c>, which redirect through a browser challenge and
        /// so only make sense for a cookie-authenticated caller in practice.
        /// Returns the <see cref="RouteGroupBuilder"/> for further customization.
        /// </summary>
        public RouteGroupBuilder MapHuiaManageEndpoints()
        {
            var group = endpoints.MapGroup("/api/identity/manage").RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme,
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());

            group.MapManageInfoEndpoints();
            group.MapManage2FaEndpoints();
            group.MapManageExternalLoginsEndpoints();

            return group;
        }

        /// <summary>
        /// Maps JSON CRUD endpoints for managing what Huia stores — OpenIddict applications and scopes, live
        /// authorizations, users, and roles — under <c>/api/identity/admin</c>.
        /// Backed by <c>IOpenIddictApplicationManager</c>/<c>IOpenIddictAuthorizationManager</c>/
        /// <c>UserManager&lt;HuiaUser&gt;</c>/<c>RoleManager&lt;HuiaRole&gt;</c>, so it works against whatever
        /// store is configured.
        /// </summary>
        /// <remarks>
        /// Unlike the connect/manage endpoints, this is <b>not protected by any authorization policy by
        /// default</b> — chain <c>.RequireAuthorization(...)</c> onto the returned <see cref="RouteGroupBuilder"/>
        /// </remarks>
        public RouteGroupBuilder MapHuiaAdminEndpoints()
        {
            var group = endpoints.MapGroup("/api/identity/admin");

            group.MapApplicationsEndpoints();
            group.MapAuthorizationsEndpoints();
            group.MapScopesEndpoints();
            group.MapUsersEndpoints();
            group.MapRolesEndpoints();

            return group;
        }
    }
}