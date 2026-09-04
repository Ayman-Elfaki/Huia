using System.ComponentModel.DataAnnotations;
using System.Text;
using Huia.AspNetCore.Identity;
using Huia.AspNetCore.UI;
using Huia.EntityFrameworkCore.Entities;
using Huia.Events;
using Finbuckle.MultiTenant.Abstractions;
using Huia.EntityFrameworkCore.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace Huia.AspNetCore.Areas.Identity.Pages.Account;

/// <summary>Sets a new password from a reset link.</summary>
public sealed class ResetPasswordModel(
    IHuiaFlowIdentityFactory flowIdentity,
    IMultiTenantContextAccessor tenantAccessor,
    IHuiaEventPublisher events,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : HuiaAccountPageModel
{
    /// <summary>The bound reset fields.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Whether the reset completed.</summary>
    public bool Done { get; private set; }

    /// <summary>Handles the GET; carries the link parameters into hidden fields.</summary>
    /// <param name="userId">The user id from the link.</param>
    /// <param name="code">The Base64Url-encoded reset token.</param>
    /// <returns>The page, or 404 when parameters are missing.</returns>
    public IActionResult OnGet(string? userId, string? code)
    {
        SetHeadings();
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
        {
            return NotFound();
        }

        Input = new InputModel { UserId = userId, Code = code };
        return Page();
    }

    /// <summary>Handles the POST that sets the new password.</summary>
    /// <returns>The page with a confirmation or errors.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        SetHeadings();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userManager = flowIdentity.Create(HuiaAuthFlow.EmailAndPasswordLogin).UserManager;
        var user = await userManager.FindByIdAsync(Input.UserId);
        if (user is not null)
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code));
            var result = await userManager.ResetPasswordAsync(user, token, Input.Password);
            if (result.Succeeded)
            {
                await events.PublishAsync(new PasswordChangedEvent(
                    tenantAccessor.RequireCurrentTenantId(), user.Id, Reset: true, timeProvider.GetUtcNow()));
            }
            else
            {
                ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                return Page();
            }
        }

        // Report success regardless of whether the user existed.
        Done = true;
        return Page();
    }

    private void SetHeadings()
    {
        ViewData["Title"] = localizer["ForgotPassword.Title"].Value;
        ViewData["Heading"] = localizer["ForgotPassword.Heading"].Value;
    }

    /// <summary>The reset form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>The user id from the link.</summary>
        [Required]
        public string UserId { get; set; } = string.Empty;

        /// <summary>The reset token from the link.</summary>
        [Required]
        public string Code { get; set; } = string.Empty;

        /// <summary>The new password.</summary>
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        /// <summary>Confirmation of the new password.</summary>
        [DataType(DataType.Password)]
        [Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
