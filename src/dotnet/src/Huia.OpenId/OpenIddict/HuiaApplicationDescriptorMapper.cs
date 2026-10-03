using System.Globalization;
using System.Text.Json;
using Huia.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.OpenId.OpenIddict;

/// <summary>
/// Translates a <see cref="HuiaApplication"/> into an <see cref="OpenIddictApplicationDescriptor"/>.
/// Shared by <see cref="HuiaClientSeeder"/> (options-tree clients, stamped <c>huia:origin = static</c>)
/// and the admin API (runtime clients, stamped <c>dynamic</c>).
/// </summary>
internal static class HuiaApplicationDescriptorMapper
{
    /// <summary>Builds the OpenIddict descriptor for an application.</summary>
    /// <param name="tenantId">The owning tenant, written to <c>Properties["huia:tenant"]</c>.</param>
    /// <param name="client">The source application.</param>
    /// <param name="origin">Either <see cref="HuiaConstants.Origins.Static"/> or <c>Dynamic</c>.</param>
    /// <returns>A fully populated descriptor ready for <c>IOpenIddictApplicationManager.CreateAsync</c>.</returns>
    public static OpenIddictApplicationDescriptor ToDescriptor(string tenantId, HuiaApplication client, string origin)
    {
        var clientSecret = GetClientSecret(client);
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            ClientSecret = client.IsPublic ? null : clientSecret,
            ClientType = client.IsPublic ? ClientTypes.Public : ClientTypes.Confidential,
            DisplayName = client.DisplayName ?? client.ClientId,
            ConsentType = client.RequireConsent ? ConsentTypes.Explicit : ConsentTypes.Implicit,
        };

        if (client is InteractiveClientApplication interactive)
        {
            foreach (var uri in interactive.RedirectUris)
            {
                descriptor.RedirectUris.Add(uri);
            }

            foreach (var uri in interactive.PostLogoutRedirectUris)
            {
                descriptor.PostLogoutRedirectUris.Add(uri);
            }

            if (interactive.HomeUris.Count > 0)
            {
                descriptor.Properties[HuiaConstants.ApplicationProperties.HomeUris] =
                    JsonSerializer.SerializeToElement(interactive.HomeUris.Select(u => u.ToString()).ToArray());
            }
        }

        ApplyPermissions(descriptor, client);

        if (GetRequiresPkce(client))
        {
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        if (GetRequiresPar(client))
        {
            descriptor.Requirements.Add(Requirements.Features.PushedAuthorizationRequests);
        }

        descriptor.Properties[HuiaConstants.ApplicationProperties.Tenant] =
            JsonSerializer.SerializeToElement(tenantId);
        descriptor.Properties[HuiaConstants.ApplicationProperties.Origin] =
            JsonSerializer.SerializeToElement(origin);

        if (client.ClientUri is not null)
        {
            descriptor.Properties[HuiaConstants.ApplicationProperties.ClientUri] =
                JsonSerializer.SerializeToElement(client.ClientUri.ToString());
        }

        if (client.LogoUri is not null)
        {
            descriptor.Properties[HuiaConstants.ApplicationProperties.LogoUri] =
                JsonSerializer.SerializeToElement(client.LogoUri.ToString());
        }

        ApplyTokenLifetimes(descriptor, client.Token);
        return descriptor;
    }

    private static string? GetClientSecret(HuiaApplication client) => client switch
    {
        ServerSideWebApplication web => web.ClientSecret,
        MachineToMachineApplication m2m => m2m.ClientSecret,
        DeviceApplication dev => dev.ClientSecret,
        HuiaClientDescriptor desc => desc.ClientSecret,
        _ => null,
    };

    private static bool GetRequiresPkce(HuiaApplication client) => client switch
    {
        ServerSideWebApplication web => web.RequirePkce,
        SinglePageApplication => true,
        NativeApplication => true,
        DeviceApplication dev => dev.RequirePkce,
        HuiaClientDescriptor desc => desc.RequirePkce || desc.IsPublic,
        _ => false,
    };

    private static bool GetRequiresPar(HuiaApplication client) => client switch
    {
        ServerSideWebApplication web => web.RequiresPushedAuthorizationRequests,
        HuiaClientDescriptor desc => desc.RequiresPushedAuthorizationRequests,
        _ => false,
    };

    private static void ApplyPermissions(OpenIddictApplicationDescriptor descriptor, HuiaApplication client)
    {
        descriptor.Permissions.Add(Permissions.Endpoints.Token);

        switch (client.Kind)
        {
            case ClientKind.MachineToMachine:
                descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
                break;

            case ClientKind.Device:
                descriptor.Permissions.Add(Permissions.Endpoints.DeviceAuthorization);
                descriptor.Permissions.Add(Permissions.GrantTypes.DeviceCode);
                descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
                break;

            default:
                descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
                descriptor.Permissions.Add(Permissions.Endpoints.PushedAuthorization);
                descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
                descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
                descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
                descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
                break;
        }

        descriptor.Permissions.Add(Permissions.Scopes.Email);
        descriptor.Permissions.Add(Permissions.Scopes.Profile);
        descriptor.Permissions.Add(Permissions.Scopes.Roles);

        if (descriptor.Permissions.Contains(Permissions.GrantTypes.RefreshToken))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + Scopes.OfflineAccess);
        }

        foreach (var scope in client.Scopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }
    }

    private static void ApplyTokenLifetimes(OpenIddictApplicationDescriptor descriptor, TokenLifetimeOptions token)
    {
        void Set(string key, TimeSpan? value)
        {
            if (value is { } span)
            {
                descriptor.Settings[key] = span.ToString("c", CultureInfo.InvariantCulture);
            }
        }

        Set(Settings.TokenLifetimes.AccessToken, token.AccessToken);
        Set(Settings.TokenLifetimes.IdentityToken, token.IdentityToken);
        Set(Settings.TokenLifetimes.RefreshToken, token.RefreshToken);
        Set(Settings.TokenLifetimes.AuthorizationCode, token.AuthorizationCode);
        Set(Settings.TokenLifetimes.DeviceCode, token.DeviceCode);
        Set(Settings.TokenLifetimes.UserCode, token.UserCode);
    }
}
