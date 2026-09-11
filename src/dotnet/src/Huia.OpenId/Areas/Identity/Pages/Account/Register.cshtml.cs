using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.Emails;
using Huia.OpenId.Flows;
using Huia.OpenId.Identity;
using Huia.OpenId.UI;
using Huia.Events;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace Huia.OpenId.Areas.Identity.Pages.Account;

/// <summary>Self-service account creation. Available only when the tenant enables it.</summary>
public sealed class RegisterModel(
    IHuiaFlowIdentityFactory flowIdentity,
    IReturnUrlProtector returnUrlProtector,
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaEventPublisher events,
    IHuiaEmailSender emailSender,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : HuiaAccountPageModel
{
    /// <summary>The bound registration fields.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The sanitized return URL carried through the form.</summary>
    public string ReturnUrl { get; private set; } = "/";

    /// <summary>Whether self-service registration is available for this tenant.</summary>
    public bool RegistrationEnabled => Tenant?.Authentication.EmailAndPassword.AllowSelfServiceRegistration ?? false;

    /// <summary>Handles the initial GET.</summary>
    /// <param name="returnUrl">The URL to return to after registering.</param>
    /// <returns>The page, or 404 when registration is disabled.</returns>
    public IActionResult OnGet(string? returnUrl)
    {
        SetHeadings();
        ReturnUrl = returnUrlProtector.SanitizeReturnUrl(returnUrl, HttpContext);
        return RegistrationEnabled ? Page() : NotFound();
    }

    /// <summary>Handles the registration POST.</summary>
    /// <param name="returnUrl">The URL to return to after registering.</param>
    /// <returns>A redirect on success, otherwise the page with errors.</returns>
    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        SetHeadings();
        ReturnUrl = returnUrlProtector.SanitizeReturnUrl(returnUrl, HttpContext);

        if (!RegistrationEnabled)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var tenantId = tenantAccessor.RequireCurrentTenantId();
        var password = flowIdentity.Create(HuiaAuthFlow.EmailAndPasswordLogin);
        var user = new HuiaUser
        {
            TenantId = tenantId,
            UserName = Input.Email,
            Email = Input.Email,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
        };

        var result = await password.UserManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }

        await events.PublishAsync(new UserRegisteredEvent(
            tenantId, user.Id, user.UserName!, user.Email, HuiaConstants.AuthenticationMethods.Password, timeProvider.GetUtcNow()));

        if (!(Tenant?.Authentication.EmailAndPassword.RequireConfirmedEmail ?? true))
        {
            await password.SignInManager.SignInAsync(user, isPersistent: false);
            return await ResolvePostSignUpRedirectAsync(user, ReturnUrl);
        }

        var rawToken = await password.UserManager.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
        var confirmUrl = Url.Page("./ConfirmEmail", pageHandler: null,
            values: new { userId = user.Id, code }, protocol: Request.Scheme)!;
        await emailSender.SendEmailConfirmationAsync(user, confirmUrl, HttpContext.RequestAborted);

        return RedirectToPage("./RegisterConfirmation", new { returnUrl = ReturnUrl });
    }

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["Register.Title"].Value;
        ViewData["Heading"] = localizer["Register.Heading"].Value;
    }

    /// <summary>The registration form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>Given name.</summary>
        [Required]
        public string FirstName { get; set; } = string.Empty;

        /// <summary>Family name.</summary>
        [Required]
        public string LastName { get; set; } = string.Empty;

        /// <summary>Email address (also the username).</summary>
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        /// <summary>The chosen password.</summary>
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        /// <summary>Confirmation of the chosen password.</summary>
        [DataType(DataType.Password)]
        [Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
