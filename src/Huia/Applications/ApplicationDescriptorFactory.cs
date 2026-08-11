using System.Text.Json;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.Applications;

/// <summary>
/// Builds <see cref="OpenIddictApplicationDescriptor"/>s for each client-application kind Huia knows about,
/// with the right permissions/requirements for that kind. Shared by <see cref="ApplicationInitializer"/>
/// (declarative registration via <see cref="ApplicationsBuilder"/>) and <c>Huia.AdminUI</c>'s
/// application-management endpoints, so both go through identical permission/requirement logic.
/// </summary>
internal static class ApplicationDescriptorFactory
{
    public static OpenIddictApplicationDescriptor BuildPublicInteractiveDescriptor(ClientApplicationOptions app,
        string applicationType)
    {
        var descriptor = NewDescriptor(app, applicationType, ClientTypes.Public);

        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
        descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);

        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

        AddRedirectUris(descriptor, app);
        AddScopes(descriptor, app, includeOfflineAccess: true);

        return descriptor;
    }

    public static OpenIddictApplicationDescriptor BuildServerSideWebDescriptor(ServerSideWebApplicationOptions app)
    {
        var descriptor = NewDescriptor(app, ApplicationTypes.Web, ClientTypes.Confidential);

        descriptor.ClientSecret = app.ClientSecret;

        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
        descriptor.Permissions.Add(Permissions.Endpoints.Introspection);
        descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);

        if (app.RequiresPkce)
        {
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        AddRedirectUris(descriptor, app);
        AddScopes(descriptor, app, includeOfflineAccess: true);

        return descriptor;
    }

    public static OpenIddictApplicationDescriptor BuildMachineToMachineDescriptor(
        MachineToMachineApplicationOptions app)
    {
        var descriptor = NewDescriptor(app, ApplicationTypes.Web,
            ClientTypes.Confidential);
        descriptor.ClientSecret = app.ClientSecret;
        descriptor.ConsentType = ConsentTypes.Systematic;

        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);

        AddScopes(descriptor, app, includeOfflineAccess: false);

        return descriptor;
    }

    public static OpenIddictApplicationDescriptor BuildDeviceDescriptor(DeviceApplicationOptions app)
    {
        var descriptor = NewDescriptor(app, ApplicationTypes.Native,
            ClientTypes.Public);

        descriptor.Permissions.Add(Permissions.Endpoints.DeviceAuthorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.GrantTypes.DeviceCode);
        descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);

        AddScopes(descriptor, app, includeOfflineAccess: true);

        return descriptor;
    }

    public static OpenIddictApplicationDescriptor NewDescriptor(ClientApplicationOptions app, string applicationType,
        string clientType)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = app.ClientId,
            DisplayName = app.DisplayName ?? app.ClientId,
            ApplicationType = applicationType,
            ClientType = clientType,
            ConsentType = app.IsTrusted
                ? ConsentTypes.Implicit
                : ConsentTypes.Explicit,
        };

        if (app.HomeUri is not null)
        {
            descriptor.Properties[HomeUriApplicationProperty.HomeUri] =
                JsonSerializer.SerializeToElement(app.HomeUri);
        }

        if (app.AccessTokenLifetime is not null)
        {
            descriptor.SetAccessTokenLifetime(app.AccessTokenLifetime);
        }

        if (app.IdentityTokenLifetime is not null)
        {
            descriptor.SetIdentityTokenLifetime(app.IdentityTokenLifetime);
        }

        if (app.RefreshTokenLifetime is not null)
        {
            descriptor.SetRefreshTokenLifetime(app.RefreshTokenLifetime);
        }

        return descriptor;
    }

    public static void AddRedirectUris(OpenIddictApplicationDescriptor descriptor, ClientApplicationOptions app)
    {
        foreach (var uri in app.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        if (app.PostLogoutRedirectUris.Count > 0)
        {
            // Without this permission, OpenIddict's /connect/logout endpoint rejects every request for this
            // client with a generic "invalid post_logout_redirect_uri" error regardless of whether the URI
            // itself is registered — it never gets far enough to check that.
            descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
        }

        foreach (var uri in app.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }
    }

    private static void AddScopes(OpenIddictApplicationDescriptor descriptor, ClientApplicationOptions app,
        bool includeOfflineAccess)
    {
        string[] wellKnownScopes = includeOfflineAccess
            ?
            [
                OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Scopes.OfflineAccess,
            ]
            : [OpenIddictConstants.Scopes.OpenId];

        descriptor.AddScopePermissions([.. wellKnownScopes, .. app.Scopes]);
    }

    public static async Task SeedAsync(IOpenIddictApplicationManager manager,
        OpenIddictApplicationDescriptor descriptor, CancellationToken ct)
    {
        var existing = await manager.FindByClientIdAsync(descriptor.ClientId!, ct).ConfigureAwait(false);

        if (existing is null)
        {
            await manager.CreateAsync(descriptor, ct).ConfigureAwait(false);
        }
        else
        {
            await manager.UpdateAsync(existing, descriptor, ct).ConfigureAwait(false);
        }
    }
}