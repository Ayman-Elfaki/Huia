using Huia.Applications;
using Huia.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.Areas.Identity.Pages;

/// <summary>
/// Renders a branded error page for whatever status code brought the request here — in particular, this is
/// where OpenIddict's connect endpoints (<c>EnableStatusCodePagesIntegration()</c>, see
/// <c>ServiceCollectionExtensions.cs</c>) send requests that fail in a way that can't be redirected back to
/// the client, e.g. an unknown <c>client_id</c> or <c>redirect_uri</c>. Wired up automatically by
/// <c>app.UseHuia()</c>.
/// </summary>
/// <remarks>
/// <see cref="IgnoreAntiforgeryTokenAttribute"/>: Razor Pages auto-validates an antiforgery token on every
/// unsafe HTTP method by convention, which is right for a real form post but wrong here — this page is only
/// ever reached via <c>UseStatusCodePagesWithReExecute</c> replaying an already-decided request (a JSON API
/// call that failed with an empty-bodied 401/403/404, say), never a form submission, so it never carries
/// one. Without this, a POST/PUT/DELETE re-executed here fails antiforgery validation and the client sees an
/// unrelated 400 instead of its real status code.
/// </remarks>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class ErrorModel(IStringLocalizer<HuiaResources> localizer, IOpenIddictApplicationManager applicationManager)
    : PageModel
{
    /// <summary>The status code the request originally failed with.</summary>
    public int Code { get; private set; }

    /// <summary>A short, status-appropriate heading.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>A plain-language explanation to show alongside <see cref="Title"/>.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// A safe "return home" destination for the client named by the original failing request's own
    /// <c>client_id</c> parameter — <see langword="null"/> if there isn't one, it doesn't name a registered
    /// client, or that client has neither a <see cref="Huia.Applications.ClientApplicationOptions.HomeUri"/>
    /// nor a registered redirect_uri to fall back to.
    /// </summary>
    public string? HomeUrl { get; private set; }

    /// <summary>
    /// Renders the page for <paramref name="statusCode"/>. Handles every HTTP method, not just GET:
    /// <c>UseStatusCodePagesWithReExecute</c> (<c>ApplicationBuilderExtensions.UseHuia</c>) replays the
    /// original request's method when it re-executes here, and a JSON API endpoint failing on a
    /// POST/PUT/DELETE with an empty-bodied 401/403/404 lands on this page exactly like a browser's GET
    /// does — without a matching handler for that verb, Razor Pages has nothing to invoke and the
    /// re-executed request falls through as an unrelated 400 instead of preserving the original status.
    /// </summary>
    public Task OnGetAsync(int? statusCode) => RenderAsync(statusCode);

    /// <inheritdoc cref="OnGetAsync"/>
    public Task OnPostAsync(int? statusCode) => RenderAsync(statusCode);

    /// <inheritdoc cref="OnGetAsync"/>
    public Task OnPutAsync(int? statusCode) => RenderAsync(statusCode);

    /// <inheritdoc cref="OnGetAsync"/>
    public Task OnDeleteAsync(int? statusCode) => RenderAsync(statusCode);

    /// <inheritdoc cref="OnGetAsync"/>
    public Task OnPatchAsync(int? statusCode) => RenderAsync(statusCode);

    private async Task RenderAsync(int? statusCode)
    {
        Code = statusCode ?? StatusCodes.Status500InternalServerError;

        var (titleKey, messageKey) = Code switch
        {
            StatusCodes.Status400BadRequest => ("ErrorBadRequestTitle", "ErrorBadRequestMessage"),
            StatusCodes.Status401Unauthorized => ("ErrorUnauthorizedTitle", "ErrorUnauthorizedMessage"),
            StatusCodes.Status403Forbidden => ("ErrorForbiddenTitle", "ErrorForbiddenMessage"),
            StatusCodes.Status404NotFound => ("ErrorNotFoundTitle", "ErrorNotFoundMessage"),
            _ => ("ErrorGenericTitle", "ErrorGenericMessage"),
        };

        Title = localizer[titleKey];
        Message = localizer[messageKey];

        // UseStatusCodePagesWithReExecute rewrites Request.Path/QueryString to this page's own route (no
        // query string of its own here) — the request that actually failed is only available via this
        // feature, as OriginalPath/OriginalQueryString.
        var originalQueryString = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalQueryString;
        var clientId = string.IsNullOrEmpty(originalQueryString)
            ? null
            : QueryHelpers.ParseQuery(originalQueryString).TryGetValue(Parameters.ClientId, out var value)
                ? value.ToString()
                : null;

        HomeUrl = await ApplicationHomeUriResolver.ResolveFromClientIdAsync(clientId, applicationManager,
            HttpContext.RequestAborted);
    }
}