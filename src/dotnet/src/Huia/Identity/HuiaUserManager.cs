using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.Identity;

/// <summary>
/// The Huia <see cref="UserManager{TUser}"/>. Beyond the stock surface it consolidates the phone
/// (SMS one-time code) and external-provider account operations that were otherwise open-coded across
/// the account UI and the <c>/manage</c> and external-login endpoints: classification
/// (<see cref="GetUserTypeAsync"/>), phone lookup / provisioning, and external link-or-create.
/// </summary>
public partial class HuiaUserManager : UserManager<HuiaUser>
{
    /// <summary>Creates the manager. Parameters are the stock <see cref="UserManager{TUser}"/> dependencies.</summary>
    public HuiaUserManager(
        IUserStore<HuiaUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<HuiaUser> passwordHasher,
        IEnumerable<IUserValidator<HuiaUser>> userValidators,
        IEnumerable<IPasswordValidator<HuiaUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<HuiaUser>> logger)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
    {
    }

    /// <summary>
    /// Resolves the account's <see cref="HuiaUserType"/>. Precedence is password → external → phone:
    /// a record with a password is a <see cref="HuiaUserType.Password"/> account even if it also has an
    /// external login.
    /// </summary>
    public async Task<HuiaUserType> GetUserTypeAsync(HuiaUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (await HasPasswordAsync(user))
        {
            return HuiaUserType.Password;
        }

        if ((await GetLoginsAsync(user)).Count > 0)
        {
            return HuiaUserType.External;
        }

        return string.IsNullOrEmpty(user.PhoneNumber) ? HuiaUserType.Unknown : HuiaUserType.Phone;
    }

    /// <summary>The number of passkeys (WebAuthn credentials) registered to <paramref name="user"/>.</summary>
    public async Task<int> CountPasskeysAsync(HuiaUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return (await GetPasskeysAsync(user)).Count;
    }

    /// <summary>Whether <paramref name="user"/> has at least one registered passkey.</summary>
    public async Task<bool> HasPasskeyAsync(HuiaUser user) => await CountPasskeysAsync(user) > 0;

    /// <summary>
    /// Renames one of the user's passkeys. Returns <see langword="false"/> when no credential with
    /// <paramref name="credentialId"/> belongs to the user.
    /// </summary>
    public async Task<bool> RenamePasskeyAsync(HuiaUser user, byte[] credentialId, string name)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(credentialId);

        var passkey = await GetPasskeyAsync(user, credentialId);
        if (passkey is null)
        {
            return false;
        }

        passkey.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var result = await AddOrUpdatePasskeyAsync(user, passkey);
        return result.Succeeded;
    }

    /// <summary>
    /// True when removing one external login would still leave a way to sign in — a password, another
    /// external login, or a phone number.
    /// </summary>
    public async Task<bool> CanRemoveExternalLoginAsync(HuiaUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (await HasPasswordAsync(user) || !string.IsNullOrEmpty(user.PhoneNumber))
        {
            return true;
        }

        return (await GetLoginsAsync(user)).Count > 1;
    }

    /// <summary>
    /// True when removing one passkey would still leave a way to sign in — a password, a phone number,
    /// an external login, or another passkey.
    /// </summary>
    public async Task<bool> CanRemovePasskeyAsync(HuiaUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (await HasPasswordAsync(user) || !string.IsNullOrEmpty(user.PhoneNumber) || (await GetLoginsAsync(user)).Count > 0)
        {
            return true;
        }

        return await CountPasskeysAsync(user) > 1;
    }

    /// <summary>
    /// Finds an account by its phone number (the <c>PhoneNumber</c> column, not the username). Scoped
    /// to the current tenant by the Identity query filter.
    /// </summary>
    public Task<HuiaUser?> FindByPhoneNumberAsync(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return Task.FromResult(Users.FirstOrDefault(u => u.PhoneNumber == phoneNumber));
    }

    /// <summary>
    /// Creates a phone-login account: the E.164 number is both the username and the (confirmed) phone
    /// number, there is no password and no email.
    /// </summary>
    public async Task<HuiaUserCreation> CreatePhoneUserAsync(string tenantId, string phoneNumber, string firstName, string lastName)
    {
        var user = new HuiaUser
        {
            TenantId = tenantId,
            UserName = phoneNumber,
            PhoneNumber = phoneNumber,
            PhoneNumberConfirmed = true,
            EmailConfirmed = true, // vacuous: no email; nothing to confirm.
            FirstName = firstName,
            LastName = lastName,
        };

        return new HuiaUserCreation(await CreateAsync(user), user);
    }

    /// <summary>Attaches an external provider login to an account.</summary>
    public Task<IdentityResult> AddExternalLoginAsync(HuiaUser user, string loginProvider, string providerKey, string providerDisplayName)
        => AddLoginAsync(user, new UserLoginInfo(loginProvider, providerKey, providerDisplayName));

    /// <summary>
    /// Creates an account for a first-time external sign-in and attaches the provider login. The
    /// username is the verified email, or a slug of the provider display name + subject when there is
    /// no email. The external provider is treated as the confirmed factor.
    /// </summary>
    public async Task<HuiaUserCreation> CreateExternalUserAsync(
        string tenantId, string? email, string firstName, string lastName,
        string loginProvider, string providerKey, string providerDisplayName)
    {
        var userName = !string.IsNullOrWhiteSpace(email)
            ? email!
            : SanitizeUserName($"{Slug(providerDisplayName)}-{providerKey}");

        var user = new HuiaUser
        {
            TenantId = tenantId,
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
        };

        var result = await CreateAsync(user);
        if (result.Succeeded)
        {
            await AddExternalLoginAsync(user, loginProvider, providerKey, providerDisplayName);
        }

        return new HuiaUserCreation(result, user);
    }

    /// <summary>
    /// A logged-out external sign-in whose email matches a local account: link it when the tenant has
    /// account linking on, the local email is confirmed and the provider vouches for the address;
    /// report <see cref="ExternalEmailLinkOutcome.Blocked"/> when a match exists but is not linkable
    /// (the caller must not create a duplicate); <see cref="ExternalEmailLinkOutcome.NoMatch"/> when
    /// no account owns the email.
    /// </summary>
    public async Task<(ExternalEmailLinkOutcome Outcome, HuiaUser? User)> TryLinkExternalByEmailAsync(
        string email, bool providerVouches, bool accountLinkingEnabled,
        string loginProvider, string providerKey, string providerDisplayName)
    {
        var byEmail = await FindByEmailAsync(email);
        if (byEmail is null)
        {
            return (ExternalEmailLinkOutcome.NoMatch, null);
        }

        if (accountLinkingEnabled && byEmail.EmailConfirmed && providerVouches
            && (await AddExternalLoginAsync(byEmail, loginProvider, providerKey, providerDisplayName)).Succeeded)
        {
            return (ExternalEmailLinkOutcome.Linked, byEmail);
        }

        return (ExternalEmailLinkOutcome.Blocked, null);
    }

    private static string Slug(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "user" : NonSlugChars().Replace(value.ToLowerInvariant(), "-").Trim('-');

    private static string SanitizeUserName(string value)
    {
        var cleaned = InvalidUserNameChars().Replace(value, string.Empty);
        return string.IsNullOrWhiteSpace(cleaned) ? "user-" + Guid.NewGuid().ToString("N")[..8] : cleaned;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex("[^a-zA-Z0-9._@+-]")]
    private static partial Regex InvalidUserNameChars();
}

/// <summary>The outcome of a create/attach operation, carrying the (possibly failed) result and the account.</summary>
/// <param name="Result">The <see cref="IdentityResult"/> of the underlying <c>CreateAsync</c>.</param>
/// <param name="User">The account that was built (persisted only when <see cref="Result"/> succeeded).</param>
public readonly record struct HuiaUserCreation(IdentityResult Result, HuiaUser User)
{
    /// <summary>Whether the account was created.</summary>
    public bool Succeeded => Result.Succeeded;
}

/// <summary>The result of <see cref="HuiaUserManager.TryLinkExternalByEmailAsync"/>.</summary>
public enum ExternalEmailLinkOutcome
{
    /// <summary>No local account owns the email — the caller should provision a new account.</summary>
    NoMatch,

    /// <summary>The external login was linked to the matching local account.</summary>
    Linked,

    /// <summary>An account owns the email but linking is off or ineligible — the caller must refuse.</summary>
    Blocked,
}
