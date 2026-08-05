using System.Globalization;
using Huia.Applications;
using Huia.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static Huia.Applications.ApplicationDescriptorFactory;

namespace Huia.Endpoints.Admin;

/// <summary>
/// JSON CRUD endpoints over OpenIddict client applications, backed by <see cref="IOpenIddictApplicationManager"/>
/// (store-agnostic — works against whatever <c>IHuiaStore</c>/EF Core backend is configured). New
/// applications are built through the same <see cref="ApplicationDescriptorFactory"/> logic used
/// by Huia's own startup seeder, keyed by the same five kinds <c>ApplicationsBuilder</c> exposes.
/// </summary>
internal static class ApplicationsEndpoints
{
    public static IEndpointRouteBuilder MapApplicationsEndpoints(this IEndpointRouteBuilder api)
    {
        var applications = api.MapGroup("/applications");

        applications.MapGet("", ListAsync);
        applications.MapGet("/{id}", GetAsync);
        applications.MapPost("", CreateAsync);
        applications.MapPut("/{id}", UpdateAsync);
        applications.MapDelete("/{id}", DeleteAsync);

        return api;
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, IOpenIddictApplicationManager manager,
        CancellationToken ct)
    {
        var size = pageSize is null or <= 0 or > 100 ? 25 : pageSize.Value;
        var offset = ((page is null or <= 0 ? 1 : page.Value) - 1) * size;

        var items = new List<ApplicationResponse>();
        await foreach (var app in manager.ListAsync(size, offset, ct).ConfigureAwait(false))
        {
            items.Add(await ToResponseAsync(app, manager, ct).ConfigureAwait(false));
        }

        var totalCount = await manager.CountAsync(ct).ConfigureAwait(false);
        return Results.Ok(new PagedResult<ApplicationResponse>(items, totalCount));
    }

    private static async Task<IResult> GetAsync(string id, IOpenIddictApplicationManager manager,
        CancellationToken cancellationToken)
    {
        var app = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return app is null
            ? Results.NotFound()
            : Results.Ok(await ToResponseAsync(app, manager, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> CreateAsync(ApplicationRequest request, IOpenIddictApplicationManager manager,
        CancellationToken ct)
    {
        if (await manager.FindByClientIdAsync(request.ClientId, ct).ConfigureAwait(false) is not null)
        {
            return Results.Problem($"An application with client_id '{request.ClientId}' already exists.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var descriptorResult = TryBuildDescriptor(request, out var descriptor, out var error);
        if (!descriptorResult)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var app = await manager.CreateAsync(descriptor!, ct).ConfigureAwait(false);
        return Results.Created($"/api/applications/{request.ClientId}",
            await ToResponseAsync(app, manager, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> UpdateAsync(string id, ApplicationRequest request,
        IOpenIddictApplicationManager manager, CancellationToken cancellationToken)
    {
        var app = await manager.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (app is null)
        {
            return Results.NotFound();
        }

        if (!TryBuildDescriptor(request, out var descriptor, out var error))
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        await manager.UpdateAsync(app, descriptor!, cancellationToken).ConfigureAwait(false);
        return Results.Ok(await ToResponseAsync(app, manager, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> DeleteAsync(string id, IOpenIddictApplicationManager manager,
        CancellationToken ct)
    {
        var app = await manager.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (app is null)
        {
            return Results.NotFound();
        }

        await manager.DeleteAsync(app, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static bool TryBuildDescriptor(ApplicationRequest request, out OpenIddictApplicationDescriptor? descriptor,
        out string? error)
    {
        descriptor = null;

        try
        {
            descriptor = request.Kind switch
            {
                "spa" => BuildPublicInteractiveDescriptor(Configure(new SinglePageApplicationOptions(), request),
                    ApplicationTypes.Web),
                "native" => BuildPublicInteractiveDescriptor(Configure(new NativeApplicationOptions(), request),
                    ApplicationTypes.Native),
                "web" => BuildServerSideWebDescriptor(
                    (ServerSideWebApplicationOptions)ConfigureConfidential(new ServerSideWebApplicationOptions(),
                        request)),
                "m2m" => BuildMachineToMachineDescriptor(
                    (MachineToMachineApplicationOptions)ConfigureConfidential(new MachineToMachineApplicationOptions(),
                        request)),
                "device" => BuildDeviceDescriptor(Configure(new DeviceApplicationOptions(), request)),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown application kind"),
            };
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static TOptions Configure<TOptions>(TOptions options, ApplicationRequest request)
        where TOptions : ClientApplicationOptions
    {
        options.SetClientId(request.ClientId);
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            options.SetDisplayName(request.DisplayName);
        }

        foreach (var uri in request.RedirectUris ?? [])
        {
            options.AddRedirectUri(uri);
        }

        foreach (var uri in request.PostLogoutRedirectUris ?? [])
        {
            options.AddPostLogoutRedirectUri(uri);
        }

        if (request.Scopes is { Length: > 0 })
        {
            options.AllowScopes(request.Scopes);
        }

        if (request.RequireConsent)
        {
            options.RequireConsent();
        }

        if (request.AccessTokenLifetime is { } accessTokenLifetime)
        {
            options.SetAccessTokenLifetime(accessTokenLifetime);
        }

        if (request.IdentityTokenLifetime is { } identityTokenLifetime)
        {
            options.SetIdentityTokenLifetime(identityTokenLifetime);
        }

        if (request.RefreshTokenLifetime is { } refreshTokenLifetime)
        {
            options.SetRefreshTokenLifetime(refreshTokenLifetime);
        }

        return options;
    }

    private static ConfidentialClientApplicationOptions ConfigureConfidential(
        ConfidentialClientApplicationOptions options, ApplicationRequest request)
    {
        Configure(options, request);

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            throw new InvalidOperationException(
                "A client secret is required for confidential applications (web, m2m).");
        }

        options.SetClientSecret(request.ClientSecret);

        if (options is ServerSideWebApplicationOptions web && request.RequiresPkce)
        {
            web.RequirePkce();
        }

        return options;
    }

    private static async Task<ApplicationResponse> ToResponseAsync(object app, IOpenIddictApplicationManager manager,
        CancellationToken cancellationToken)
    {
        var permissions = await manager.GetPermissionsAsync(app, cancellationToken).ConfigureAwait(false);
        var applicationType = await manager.GetApplicationTypeAsync(app, cancellationToken).ConfigureAwait(false);
        var clientType = await manager.GetClientTypeAsync(app, cancellationToken).ConfigureAwait(false);
        var settings = await manager.GetSettingsAsync(app, cancellationToken).ConfigureAwait(false);

        return new ApplicationResponse(
            (await manager.GetIdAsync(app, cancellationToken).ConfigureAwait(false))!,
            (await manager.GetClientIdAsync(app, cancellationToken).ConfigureAwait(false))!,
            await manager.GetDisplayNameAsync(app, cancellationToken).ConfigureAwait(false),
            InferKind(applicationType, clientType, permissions),
            applicationType,
            clientType,
            await manager.GetConsentTypeAsync(app, cancellationToken).ConfigureAwait(false),
            [.. await manager.GetRedirectUrisAsync(app, cancellationToken).ConfigureAwait(false)],
            [.. await manager.GetPostLogoutRedirectUrisAsync(app, cancellationToken).ConfigureAwait(false)],
            [.. permissions],
            [.. await manager.GetRequirementsAsync(app, cancellationToken).ConfigureAwait(false)],
            GetLifetimeSetting(settings, Settings.TokenLifetimes.AccessToken),
            GetLifetimeSetting(settings, Settings.TokenLifetimes.IdentityToken),
            GetLifetimeSetting(settings, Settings.TokenLifetimes.RefreshToken));
    }

    /// <summary>
    /// Reads back a per-application token lifetime override stored under <c>Settings</c> by
    /// <see cref="OpenIddictApplicationDescriptor.SetAccessTokenLifetime"/> and its siblings — OpenIddict
    /// stores these as <see cref="TimeSpan"/>'s invariant round-trippable ("c") string format. Missing or
    /// unparseable means this client has no override and falls back to the server-wide default.
    /// </summary>
    private static TimeSpan? GetLifetimeSetting(
        System.Collections.Immutable.ImmutableDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) &&
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var lifetime)
            ? lifetime
            : null;

    private static string InferKind(string? applicationType, string? clientType,
        System.Collections.Immutable.ImmutableArray<string> permissions)
    {
        var isConfidential = string.Equals(clientType, ClientTypes.Confidential, StringComparison.OrdinalIgnoreCase);
        var isWeb = string.Equals(applicationType, ApplicationTypes.Web, StringComparison.OrdinalIgnoreCase);

        if (isConfidential)
        {
            return permissions.Contains(Permissions.GrantTypes.ClientCredentials) ? "m2m" : "web";
        }

        if (permissions.Contains(Permissions.GrantTypes.DeviceCode))
        {
            return "device";
        }

        return isWeb ? "spa" : "native";
    }

    private sealed record ApplicationResponse(
        string Id,
        string ClientId,
        string? DisplayName,
        string Kind,
        string? ApplicationType,
        string? ClientType,
        string? ConsentType,
        string[] RedirectUris,
        string[] PostLogoutRedirectUris,
        string[] Permissions,
        string[] Requirements,
        TimeSpan? AccessTokenLifetime,
        TimeSpan? IdentityTokenLifetime,
        TimeSpan? RefreshTokenLifetime);

    private sealed record ApplicationRequest(
        string Kind,
        string ClientId,
        string? DisplayName,
        string? ClientSecret,
        bool RequiresPkce,
        bool RequireConsent,
        string[]? RedirectUris,
        string[]? PostLogoutRedirectUris,
        string[]? Scopes,
        TimeSpan? AccessTokenLifetime = null,
        TimeSpan? IdentityTokenLifetime = null,
        TimeSpan? RefreshTokenLifetime = null);
}