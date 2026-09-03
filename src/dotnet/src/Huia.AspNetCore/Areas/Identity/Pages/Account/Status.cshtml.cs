using Huia.AspNetCore.UI;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>
/// Branded page for bare HTTP status codes (404, 401, 403, ...). Reached only through
/// <c>UseStatusCodePagesWithReExecute("/identity/account/status/{0}")</c>; API and protocol callers get
/// the plain status code with no HTML body.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class StatusModel(IStringLocalizer<SharedResource> localizer) : HuiaAccountPageModel
{
    private static readonly string[] ApiPathPrefixes = ["/connect", "/manage", "/admin", "/.well-known"];

    /// <summary>The status code being reported.</summary>
    public int Code { get; private set; }

    /// <summary>The body text for the resolved status code.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Handles the GET reached by the status-code-pages re-execute.</summary>
    /// <param name="code">The original response status code, taken from the route.</param>
    /// <returns>The rendered page, or a bare status code for API / protocol callers and direct hits.</returns>
    public IActionResult OnGet(int? code) => Handle(code);

    /// <summary>
    /// Handles a re-execute that kept the original request's verb (POST, PUT, ...). The rendered body
    /// is the same branded page; API callers still get the bare code.
    /// </summary>
    /// <param name="code">The original response status code, taken from the route.</param>
    /// <returns>The rendered page, or a bare status code.</returns>
    public IActionResult OnPost(int? code) => Handle(code);

    /// <summary>Handles a re-execute for any other verb.</summary>
    /// <param name="code">The original response status code, taken from the route.</param>
    /// <returns>The rendered page, or a bare status code.</returns>
    public IActionResult OnPut(int? code) => Handle(code);

    /// <summary>Handles a re-execute for the DELETE verb.</summary>
    /// <param name="code">The original response status code, taken from the route.</param>
    /// <returns>The rendered page, or a bare status code.</returns>
    public IActionResult OnDelete(int? code) => Handle(code);

    private IActionResult Handle(int? code)
    {
        var reExecute = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();

        Code = code ?? HttpContext.Response.StatusCode;
        if (Code < 400)
        {
            Code = StatusCodes.Status500InternalServerError;
        }

        // Not reached through the status-code-pages pipeline: nothing to render.
        if (reExecute is null)
        {
            return NotFound();
        }

        // API / protocol callers and anything that asked for JSON get the bare code, no HTML.
        var originalPath = reExecute.OriginalPath ?? string.Empty;
        var wantsJson = HttpContext.Request.Headers.Accept
            .Any(v => v is not null && v.Contains("application/json", StringComparison.OrdinalIgnoreCase));
        if (wantsJson || ApiPathPrefixes.Any(p => originalPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return new StatusCodeResult(Code);
        }

        HttpContext.Response.StatusCode = Code;

        var key = Code switch
        {
            400 => "400",
            401 => "401",
            403 => "403",
            404 => "404",
            500 => "500",
            _ => "Generic",
        };

        var title = localizer[$"Status.{key}.Title"].Value;
        Body = localizer[$"Status.{key}.Body"].Value;
        ViewData["Title"] = title;
        ViewData["Heading"] = title;
        return Page();
    }
}
