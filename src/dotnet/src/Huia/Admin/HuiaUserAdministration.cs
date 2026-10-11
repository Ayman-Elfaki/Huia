using System.Security.Claims;
using System.Text.RegularExpressions;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;

namespace Huia.Admin;

/// <summary>What a <see cref="HuiaUserAdministration"/> operation did.</summary>
public enum HuiaAdminOutcomeKind
{
    /// <summary>The request was valid but there was nothing to change (already in the requested state).</summary>
    Unchanged,

    /// <summary>The account was modified.</summary>
    Changed,

    /// <summary>The request was rejected as invalid; see <see cref="HuiaAdminOutcome.Field"/> and <see cref="HuiaAdminOutcome.Message"/>.</summary>
    Invalid,

    /// <summary>ASP.NET Core Identity refused the change; see <see cref="HuiaAdminOutcome.Identity"/>.</summary>
    IdentityFailed,
}

/// <summary>
/// The HTTP-agnostic result of a user-administration operation. The Headless and OpenId admin endpoints map
/// it to their own status codes and problem shapes, so each flavor keeps its wire contract while the rules
/// live here once.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Field">For <see cref="HuiaAdminOutcomeKind.Invalid"/>, the request field at fault.</param>
/// <param name="Message">For <see cref="HuiaAdminOutcomeKind.Invalid"/>, why.</param>
/// <param name="Identity">For <see cref="HuiaAdminOutcomeKind.IdentityFailed"/>, the failing result.</param>
public readonly record struct HuiaAdminOutcome(
    HuiaAdminOutcomeKind Kind, string? Field = null, string? Message = null, IdentityResult? Identity = null)
{
    /// <summary>The account was modified (publish a <c>UserUpdatedEvent</c>).</summary>
    public bool IsChanged => Kind == HuiaAdminOutcomeKind.Changed;

    /// <summary>The operation succeeded, whether or not anything changed.</summary>
    public bool IsSuccess => Kind is HuiaAdminOutcomeKind.Changed or HuiaAdminOutcomeKind.Unchanged;

    /// <summary>Nothing needed changing.</summary>
    public static HuiaAdminOutcome Unchanged { get; } = new(HuiaAdminOutcomeKind.Unchanged);

    /// <summary>The account was modified.</summary>
    public static HuiaAdminOutcome Changed { get; } = new(HuiaAdminOutcomeKind.Changed);

    /// <summary>The request was invalid.</summary>
    public static HuiaAdminOutcome Invalid(string field, string message) =>
        new(HuiaAdminOutcomeKind.Invalid, field, message);

    /// <summary>Identity refused the change.</summary>
    public static HuiaAdminOutcome Failed(IdentityResult result) =>
        new(HuiaAdminOutcomeKind.IdentityFailed, Identity: result);
}

/// <summary>The normalized account an admin "create user" request describes.</summary>
/// <param name="UserName">The sign-in name (the email, or the phone number for a phone account).</param>
/// <param name="Email">The email address, if any.</param>
/// <param name="EmailConfirmed">Whether the email starts out confirmed.</param>
/// <param name="PhoneNumber">The phone number, if any.</param>
/// <param name="PhoneNumberConfirmed">Whether the phone number starts out confirmed.</param>
/// <param name="FirstName">The first name (empty when not supplied).</param>
/// <param name="LastName">The last name (empty when not supplied).</param>
/// <param name="Password">The password, for an email-and-password account.</param>
public sealed record HuiaNewUserPlan(
    string UserName, string? Email, bool EmailConfirmed, string? PhoneNumber, bool PhoneNumberConfirmed,
    string FirstName, string LastName, string? Password)
{
    /// <summary>Whether this is an email-and-password account (otherwise a phone account).</summary>
    public bool HasPassword => Password is not null;

    /// <summary>The <see cref="HuiaConstants.AuthenticationMethods"/> value the registration event reports.</summary>
    public string AuthenticationMethod => HasPassword
        ? HuiaConstants.AuthenticationMethods.Password
        : HuiaConstants.AuthenticationMethods.Sms;

    /// <summary>Copies the planned values onto <paramref name="user"/>.</summary>
    public void ApplyTo(Entities.HuiaUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.UserName = UserName;
        user.Email = Email;
        user.EmailConfirmed = EmailConfirmed;
        user.PhoneNumber = PhoneNumber;
        user.PhoneNumberConfirmed = PhoneNumberConfirmed;
        user.FirstName = FirstName;
        user.LastName = LastName;
    }
}

/// <summary>A claim as supplied in an admin request, before validation.</summary>
/// <param name="Type">The claim type.</param>
/// <param name="Value">The claim value.</param>
public readonly record struct HuiaClaimInput(string? Type, string? Value);

/// <summary>
/// The user-administration rules shared by the single-tenant Headless and multi-tenant OpenId admin APIs:
/// claim normalization and diffing, role assignment, lock / unlock and email verification. Each method works
/// on an <see cref="UserManager{TUser}"/> that is already bound to the account's tenant, and reports an
/// <see cref="HuiaAdminOutcome"/> rather than an HTTP result.
/// </summary>
public static partial class HuiaUserAdministration
{
    /// <summary>The characters and length a role name may have.</summary>
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,256}$")]
    private static partial Regex RoleNamePattern();

    /// <summary>Returns <c>true</c> when <paramref name="name"/> is an acceptable role name.</summary>
    public static bool IsValidRoleName(string? name) => name is not null && RoleNamePattern().IsMatch(name);

    /// <summary>
    /// Trims and collects the claims of an add-claims request (one inline claim and/or a list). Fails when a
    /// listed claim has no type or when no claim was supplied at all.
    /// </summary>
    public static bool TryNormalizeClaims(
        string? type, string? value, IEnumerable<HuiaClaimInput>? claims,
        out IReadOnlyList<Claim> normalized, out HuiaAdminOutcome problem)
    {
        var result = new List<Claim>();
        normalized = result;
        problem = default;

        if (!string.IsNullOrWhiteSpace(type))
        {
            result.Add(new Claim(type.Trim(), value?.Trim() ?? string.Empty));
        }

        foreach (var claim in claims ?? [])
        {
            if (string.IsNullOrWhiteSpace(claim.Type))
            {
                problem = HuiaAdminOutcome.Invalid("claims", "Each claim must have a non-empty type.");
                return false;
            }

            result.Add(new Claim(claim.Type.Trim(), claim.Value?.Trim() ?? string.Empty));
        }

        if (result.Count == 0)
        {
            problem = HuiaAdminOutcome.Invalid("claims", "At least one claim (type and value) must be provided.");
            return false;
        }

        return true;
    }

    /// <summary>Adds the claims the user does not already have.</summary>
    public static async Task<HuiaAdminOutcome> AddClaimsAsync<TUser>(
        UserManager<TUser> userManager, TUser user, IReadOnlyList<Claim> claims)
        where TUser : class
    {
        var existing = await userManager.GetClaimsAsync(user);
        var changed = false;

        foreach (var claim in claims)
        {
            if (existing.Any(c => c.Type == claim.Type && c.Value == claim.Value))
            {
                continue;
            }

            var result = await userManager.AddClaimAsync(user, claim);
            if (!result.Succeeded)
            {
                return HuiaAdminOutcome.Failed(result);
            }

            changed = true;
        }

        return changed ? HuiaAdminOutcome.Changed : HuiaAdminOutcome.Unchanged;
    }

    /// <summary>Removes every claim of <paramref name="type"/> (and, when given, <paramref name="value"/>).</summary>
    public static async Task<HuiaAdminOutcome> RemoveClaimsAsync<TUser>(
        UserManager<TUser> userManager, TUser user, string type, string? value)
        where TUser : class
    {
        var toRemove = (await userManager.GetClaimsAsync(user))
            .Where(c => c.Type == type && (value is null || c.Value == value))
            .ToList();

        if (toRemove.Count == 0)
        {
            return HuiaAdminOutcome.Unchanged;
        }

        var result = await userManager.RemoveClaimsAsync(user, toRemove);
        return result.Succeeded ? HuiaAdminOutcome.Changed : HuiaAdminOutcome.Failed(result);
    }

    /// <summary>
    /// Adds the user to <paramref name="role"/>. Whether the role may be created on the fly or must already
    /// exist is the caller's policy and must be settled before calling.
    /// </summary>
    public static async Task<HuiaAdminOutcome> AddToRoleAsync<TUser>(
        UserManager<TUser> userManager, TUser user, string role)
        where TUser : class
    {
        if (await userManager.IsInRoleAsync(user, role))
        {
            return HuiaAdminOutcome.Unchanged;
        }

        var result = await userManager.AddToRoleAsync(user, role);
        return result.Succeeded ? HuiaAdminOutcome.Changed : HuiaAdminOutcome.Failed(result);
    }

    /// <summary>Removes the user from <paramref name="role"/> if they are in it.</summary>
    public static async Task<HuiaAdminOutcome> RemoveFromRoleAsync<TUser>(
        UserManager<TUser> userManager, TUser user, string role)
        where TUser : class
    {
        if (!await userManager.IsInRoleAsync(user, role))
        {
            return HuiaAdminOutcome.Unchanged;
        }

        var result = await userManager.RemoveFromRoleAsync(user, role);
        return result.Succeeded ? HuiaAdminOutcome.Changed : HuiaAdminOutcome.Failed(result);
    }

    /// <summary>Locks the account out until <paramref name="until"/>.</summary>
    /// <param name="userManager">The tenant-bound user manager.</param>
    /// <param name="user">The account.</param>
    /// <param name="until">When the lockout ends.</param>
    /// <param name="updateSecurityStamp">Whether to rotate the security stamp so existing sessions are invalidated.</param>
    public static async Task<HuiaAdminOutcome> LockAsync<TUser>(
        UserManager<TUser> userManager, TUser user, DateTimeOffset until, bool updateSecurityStamp)
        where TUser : class
    {
        await userManager.SetLockoutEnabledAsync(user, true);
        var result = await userManager.SetLockoutEndDateAsync(user, until);
        if (!result.Succeeded)
        {
            return HuiaAdminOutcome.Failed(result);
        }

        if (updateSecurityStamp)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        return HuiaAdminOutcome.Changed;
    }

    /// <summary>
    /// Validates the contact details of an admin "create user" request and normalizes them into a
    /// <see cref="HuiaNewUserPlan"/>: either an email with a password, or a phone number alone (never both).
    /// </summary>
    public static bool TryPlanUserCreation(
        string? email, string? password, string? phoneNumber, bool? emailConfirmed, bool? phoneNumberConfirmed,
        string? firstName, string? lastName, out HuiaNewUserPlan plan, out HuiaAdminOutcome problem)
    {
        var hasPassword = !string.IsNullOrWhiteSpace(password);
        var hasEmail = !string.IsNullOrWhiteSpace(email);
        var hasPhone = !string.IsNullOrWhiteSpace(phoneNumber);

        plan = default!;
        problem = default;

        if (hasPhone && (hasEmail || hasPassword))
        {
            problem = HuiaAdminOutcome.Invalid("contact", "A phone-login account cannot also have an email address or a password.");
            return false;
        }

        if (!hasPhone && !(hasEmail && hasPassword))
        {
            problem = HuiaAdminOutcome.Invalid("contact", "Provide an email address with a password, or a phone number.");
            return false;
        }

        plan = new HuiaNewUserPlan(
            UserName: (hasEmail ? email! : phoneNumber!).Trim(),
            Email: hasEmail ? email!.Trim() : null,
            EmailConfirmed: hasEmail && (emailConfirmed ?? false),
            PhoneNumber: hasPhone ? phoneNumber!.Trim() : null,
            PhoneNumberConfirmed: hasPhone && (phoneNumberConfirmed ?? true),
            FirstName: firstName?.Trim() ?? string.Empty,
            LastName: lastName?.Trim() ?? string.Empty,
            Password: hasPassword ? password : null);
        return true;
    }

    /// <summary>Renames a role (name validation is the caller's job; see <see cref="IsValidRoleName"/>).</summary>
    public static async Task<HuiaAdminOutcome> RenameRoleAsync<TRole>(
        RoleManager<TRole> roleManager, TRole role, string newName)
        where TRole : class
    {
        var result = await roleManager.SetRoleNameAsync(role, newName);
        if (result.Succeeded)
        {
            result = await roleManager.UpdateAsync(role);
        }

        return result.Succeeded ? HuiaAdminOutcome.Changed : HuiaAdminOutcome.Failed(result);
    }

    /// <summary>Clears the lockout.</summary>
    /// <param name="userManager">The tenant-bound user manager.</param>
    /// <param name="user">The account.</param>
    /// <param name="resetAccessFailedCount">Whether to also reset the failed-sign-in counter.</param>
    /// <param name="updateSecurityStamp">Whether to rotate the security stamp.</param>
    public static async Task<HuiaAdminOutcome> UnlockAsync<TUser>(
        UserManager<TUser> userManager, TUser user, bool resetAccessFailedCount, bool updateSecurityStamp)
        where TUser : class
    {
        var result = await userManager.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded)
        {
            return HuiaAdminOutcome.Failed(result);
        }

        if (resetAccessFailedCount)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        if (updateSecurityStamp)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        return HuiaAdminOutcome.Changed;
    }

    /// <summary>Confirms an email-and-password account's email address without a confirmation link.</summary>
    public static async Task<HuiaAdminOutcome> VerifyEmailAsync<TUser>(HuiaUserManager<TUser> userManager, TUser user)
        where TUser : Entities.HuiaUser, new()
    {
        if (await userManager.GetUserTypeAsync(user) != HuiaUserType.Password)
        {
            return HuiaAdminOutcome.Invalid("email", "Only an email-and-password account has an email address to verify.");
        }

        if (user.EmailConfirmed)
        {
            return HuiaAdminOutcome.Unchanged;
        }

        user.EmailConfirmed = true;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? HuiaAdminOutcome.Changed : HuiaAdminOutcome.Failed(result);
    }
}
