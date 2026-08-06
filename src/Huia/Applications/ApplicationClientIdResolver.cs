using OpenIddict.Abstractions;

namespace Huia.Applications;

/// <summary>
/// Resolves OpenIddict's internal application id to the application's public <c>client_id</c> — the only
/// application identifier a caller ever configured or sees elsewhere (see <c>ApplicationsEndpoints</c>).
/// Used by <c>Endpoints.Admin.AuthorizationsEndpoints</c>, which surfaces the application behind an
/// authorization instead of the opaque internal id.
/// </summary>
internal static class ApplicationClientIdResolver
{
    /// <summary>
    /// Looks up <paramref name="applicationId"/>'s <c>client_id</c>, memoizing in <paramref name="cache"/> so a
    /// page of results that shares only a handful of distinct applications doesn't re-fetch the same
    /// application once per row.
    /// </summary>
    public static async Task<string?> ResolveAsync(string? applicationId,
        IOpenIddictApplicationManager applicationManager, Dictionary<string, string?> cache,
        CancellationToken cancellationToken)
    {
        if (applicationId is null)
        {
            return null;
        }

        if (cache.TryGetValue(applicationId, out var cached))
        {
            return cached;
        }

        var application = await applicationManager.FindByIdAsync(applicationId, cancellationToken)
            .ConfigureAwait(false);
        var clientId = application is null
            ? null
            : await applicationManager.GetClientIdAsync(application, cancellationToken).ConfigureAwait(false);

        cache[applicationId] = clientId;
        return clientId;
    }
}
