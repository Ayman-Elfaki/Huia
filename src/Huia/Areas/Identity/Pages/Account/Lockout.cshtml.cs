using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Huia.Areas.Identity.Pages.Account;

/// <summary>Shown when a sign-in attempt is blocked by account lockout.</summary>
[AllowAnonymous]
public class LockoutModel : PageModel
{
    /// <summary>Renders the page.</summary>
    public void OnGet()
    {
    }
}
