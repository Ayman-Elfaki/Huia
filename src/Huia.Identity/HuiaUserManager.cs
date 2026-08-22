using System.Security.Claims;
using System.Security.Cryptography;
using Huia.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Huia.Identity;

/// <summary>
/// Huia's own user manager — replaces ASP.NET Core Identity's <c>UserManager&lt;HuiaUser&gt;</c>, kept
/// name/shape-compatible with it so existing call sites migrate by swapping the type. Resolves
/// <see cref="IHuiaUserLoginStore"/>/<see cref="IHuiaUserRoleStore"/>/<see cref="IHuiaUserTokenStore"/> by
/// casting the injected <see cref="IHuiaUserStore"/> — the same optional-capability pattern ASP.NET Core
/// Identity's own <c>UserManager</c> uses against <c>IUserStore</c>.
/// </summary>
public class HuiaUserManager(
    IHuiaUserStore store,
    IEnumerable<IHuiaTokenProvider> tokenProviders,
    IHuiaPasswordHasher passwordHasher,
    IOptions<HuiaIdentityOptions> options,
    HuiaErrorDescriber errorDescriber,
    ILogger<HuiaUserManager> logger)
{
    private readonly Dictionary<string, IHuiaTokenProvider> _tokenProviders =
        tokenProviders.ToDictionary(p => p.Name, StringComparer.Ordinal);

    private IHuiaUserLoginStore? LoginStore => store as IHuiaUserLoginStore;
    private IHuiaUserRoleStore? RoleStore => store as IHuiaUserRoleStore;
    private IHuiaUserTokenStore? TokenStore => store as IHuiaUserTokenStore;

    /// <summary>Huia's identity configuration (lockout, password policy, sign-in, token lifetimes).</summary>
    public HuiaIdentityOptions Options { get; } = options.Value;

    /// <summary>A queryable view of every user, or <see langword="null"/> if the store doesn't support it.</summary>
    public IQueryable<HuiaUser>? Users => store.Users;

    /// <summary>Whether <see cref="Users"/> is usable.</summary>
    public bool SupportsQueryableUsers => store.Users is not null;

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private IHuiaUserTokenStore RequireTokenStore() => TokenStore
        ?? throw new NotSupportedException("The configured store doesn't implement IHuiaUserTokenStore.");

    private IHuiaUserLoginStore RequireLoginStore() => LoginStore
        ?? throw new NotSupportedException("The configured store doesn't implement IHuiaUserLoginStore.");

    private IHuiaUserRoleStore RequireRoleStore() => RoleStore
        ?? throw new NotSupportedException("The configured store doesn't implement IHuiaUserRoleStore.");

    private IHuiaTokenProvider RequireProvider(string name) => _tokenProviders.TryGetValue(name, out var provider)
        ? provider
        : throw new NotSupportedException($"No IHuiaTokenProvider named '{name}' is registered.");

    #region Lookup

    /// <summary>Finds a user by id, or <see langword="null"/> if none exists.</summary>
    public Task<HuiaUser?> FindByIdAsync(string userId) => store.FindByIdAsync(userId, CancellationToken.None);

    /// <summary>Finds a user by email (case-insensitive), or <see langword="null"/> if none exists.</summary>
    public Task<HuiaUser?> FindByEmailAsync(string email) =>
        store.FindByNormalizedEmailAsync(Normalize(email), CancellationToken.None);

    /// <summary>Finds a user by username (case-insensitive), or <see langword="null"/> if none exists.</summary>
    public Task<HuiaUser?> FindByNameAsync(string userName) =>
        store.FindByNormalizedUserNameAsync(Normalize(userName), CancellationToken.None);

    /// <summary>Resolves the signed-in user from <paramref name="principal"/>'s <see cref="ClaimTypes.NameIdentifier"/>
    /// claim, or <see langword="null"/> if absent/unresolvable.</summary>
    public Task<HuiaUser?> GetUserAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? Task.FromResult<HuiaUser?>(null) : FindByIdAsync(userId);
    }

    /// <summary>The user's id.</summary>
    public Task<string> GetUserIdAsync(HuiaUser user) => Task.FromResult(user.Id);

    /// <summary>The user's username.</summary>
    public Task<string?> GetUserNameAsync(HuiaUser user) => Task.FromResult(user.UserName);

    /// <summary>The user's email address.</summary>
    public Task<string?> GetEmailAsync(HuiaUser user) => Task.FromResult(user.Email);

    /// <summary>Whether the user's email has been confirmed.</summary>
    public Task<bool> IsEmailConfirmedAsync(HuiaUser user) => Task.FromResult(user.EmailConfirmed);

    /// <summary>Whether the user's phone number has been confirmed.</summary>
    public Task<bool> IsPhoneNumberConfirmedAsync(HuiaUser user) => Task.FromResult(user.PhoneNumberConfirmed);

    #endregion

    #region Create / update / delete

    /// <summary>Creates a password-less user (no password set).</summary>
    public async Task<HuiaIdentityResult> CreateAsync(HuiaUser user)
    {
        var validation = await ValidateUserAsync(user).ConfigureAwait(false);
        if (!validation.Succeeded)
        {
            return validation;
        }

        NormalizeUser(user);

        // Mirrors ASP.NET Core Identity's own UserManager.CreateAsync: a brand-new account gets lockout
        // enabled automatically when LockoutOptions.AllowedForNewUsers is set (the default), so
        // AccessFailedAsync's threshold actually takes effect without every caller having to remember to
        // call SetLockoutEnabledAsync itself.
        if (Options.Lockout.AllowedForNewUsers)
        {
            user.LockoutEnabled = true;
        }

        try
        {
            await store.CreateAsync(user, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HuiaConcurrencyException)
        {
            return HuiaIdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        return HuiaIdentityResult.Success;
    }

    /// <summary>Creates a user with <paramref name="password"/> set.</summary>
    public async Task<HuiaIdentityResult> CreateAsync(HuiaUser user, string password)
    {
        var passwordValidation = ValidatePassword(password);
        if (!passwordValidation.Succeeded)
        {
            return passwordValidation;
        }

        user.PasswordHash = passwordHasher.HashPassword(password);
        return await CreateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Persists changes to an existing user, regenerating its concurrency stamp.</summary>
    public async Task<HuiaIdentityResult> UpdateAsync(HuiaUser user)
    {
        NormalizeUser(user);
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        try
        {
            await store.UpdateAsync(user, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HuiaConcurrencyException)
        {
            return HuiaIdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        return HuiaIdentityResult.Success;
    }

    /// <summary>Deletes a user.</summary>
    public async Task<HuiaIdentityResult> DeleteAsync(HuiaUser user)
    {
        try
        {
            await store.DeleteAsync(user, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HuiaConcurrencyException)
        {
            return HuiaIdentityResult.Failed(errorDescriber.ConcurrencyFailure());
        }

        return HuiaIdentityResult.Success;
    }

    private static void NormalizeUser(HuiaUser user)
    {
        if (user.UserName is not null)
        {
            user.NormalizedUserName = Normalize(user.UserName);
        }

        if (user.Email is not null)
        {
            user.NormalizedEmail = Normalize(user.Email);
        }
    }

    private async Task<HuiaIdentityResult> ValidateUserAsync(HuiaUser user)
    {
        var errors = new List<HuiaIdentityError>();

        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            errors.Add(errorDescriber.InvalidUserName(user.UserName));
        }
        else
        {
            var existing = await store.FindByNormalizedUserNameAsync(Normalize(user.UserName), CancellationToken.None)
                .ConfigureAwait(false);
            if (existing is not null && !string.Equals(existing.Id, user.Id, StringComparison.Ordinal))
            {
                errors.Add(errorDescriber.DuplicateUserName(user.UserName));
            }
        }

        if (!string.IsNullOrEmpty(user.Email))
        {
            if (!user.Email.Contains('@', StringComparison.Ordinal))
            {
                errors.Add(errorDescriber.InvalidEmail(user.Email));
            }
            else
            {
                var existing = await store.FindByNormalizedEmailAsync(Normalize(user.Email), CancellationToken.None)
                    .ConfigureAwait(false);
                if (existing is not null && !string.Equals(existing.Id, user.Id, StringComparison.Ordinal))
                {
                    errors.Add(errorDescriber.DuplicateEmail(user.Email));
                }
            }
        }

        return errors.Count == 0 ? HuiaIdentityResult.Success : HuiaIdentityResult.Failed(errors);
    }

    #endregion

    #region Password

    /// <summary>Whether the user has a password set.</summary>
    public Task<bool> HasPasswordAsync(HuiaUser user) => Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    /// <summary>Sets a password on a user that doesn't already have one.</summary>
    public async Task<HuiaIdentityResult> AddPasswordAsync(HuiaUser user, string password)
    {
        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            return HuiaIdentityResult.Failed(errorDescriber.UserAlreadyHasPassword());
        }

        var validation = ValidatePassword(password);
        if (!validation.Succeeded)
        {
            return validation;
        }

        user.PasswordHash = passwordHasher.HashPassword(password);
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Changes the user's password after verifying <paramref name="currentPassword"/>.</summary>
    public async Task<HuiaIdentityResult> ChangePasswordAsync(HuiaUser user, string currentPassword, string newPassword)
    {
        if (!await VerifyPasswordAsync(user, currentPassword).ConfigureAwait(false))
        {
            return HuiaIdentityResult.Failed(errorDescriber.PasswordMismatch());
        }

        var validation = ValidatePassword(newPassword);
        if (!validation.Succeeded)
        {
            return validation;
        }

        user.PasswordHash = passwordHasher.HashPassword(newPassword);
        await UpdateSecurityStampInternalAsync(user).ConfigureAwait(false);
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Verifies <paramref name="password"/> against the user's stored hash.</summary>
    internal Task<bool> VerifyPasswordAsync(HuiaUser user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return Task.FromResult(false);
        }

        var result = passwordHasher.VerifyHashedPassword(user.PasswordHash, password);
        return Task.FromResult(result != HuiaPasswordVerificationResult.Failed);
    }

    private HuiaIdentityResult ValidatePassword(string password)
    {
        var policy = Options.Password;
        var errors = new List<HuiaIdentityError>();

        if (password.Length < policy.RequiredLength)
        {
            errors.Add(errorDescriber.PasswordTooShort(policy.RequiredLength));
        }

        if (policy.RequiredUniqueChars >= 1 && password.Distinct().Count() < policy.RequiredUniqueChars)
        {
            errors.Add(errorDescriber.PasswordRequiresUniqueChars(policy.RequiredUniqueChars));
        }

        if (policy.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            errors.Add(errorDescriber.PasswordRequiresNonAlphanumeric());
        }

        if (policy.RequireDigit && !password.Any(char.IsDigit))
        {
            errors.Add(errorDescriber.PasswordRequiresDigit());
        }

        if (policy.RequireLowercase && !password.Any(char.IsLower))
        {
            errors.Add(errorDescriber.PasswordRequiresLower());
        }

        if (policy.RequireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add(errorDescriber.PasswordRequiresUpper());
        }

        return errors.Count == 0 ? HuiaIdentityResult.Success : HuiaIdentityResult.Failed(errors);
    }

    #endregion

    #region Security stamp

    /// <summary>Regenerates and persists the user's security stamp — invalidates outstanding sessions/tokens.</summary>
    public async Task<HuiaIdentityResult> UpdateSecurityStampAsync(HuiaUser user)
    {
        await UpdateSecurityStampInternalAsync(user).ConfigureAwait(false);
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    private static Task UpdateSecurityStampInternalAsync(HuiaUser user)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    #endregion

    #region Tokens (reset / confirmation / phone change)

    /// <summary>Generates a password-reset token.</summary>
    public Task<string> GeneratePasswordResetTokenAsync(HuiaUser user) =>
        RequireProvider(Options.Tokens.DefaultProvider).GenerateAsync(user, "ResetPassword", CancellationToken.None);

    /// <summary>Resets the user's password if <paramref name="token"/> is valid.</summary>
    public async Task<HuiaIdentityResult> ResetPasswordAsync(HuiaUser user, string token, string newPassword)
    {
        var valid = await RequireProvider(Options.Tokens.DefaultProvider)
            .ValidateAsync(user, "ResetPassword", token, CancellationToken.None).ConfigureAwait(false);
        if (!valid)
        {
            return HuiaIdentityResult.Failed(errorDescriber.InvalidToken());
        }

        var validation = ValidatePassword(newPassword);
        if (!validation.Succeeded)
        {
            return validation;
        }

        user.PasswordHash = passwordHasher.HashPassword(newPassword);
        await UpdateSecurityStampInternalAsync(user).ConfigureAwait(false);
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Generates an email-confirmation token.</summary>
    public Task<string> GenerateEmailConfirmationTokenAsync(HuiaUser user) =>
        RequireProvider(Options.Tokens.DefaultProvider).GenerateAsync(user, "EmailConfirmation", CancellationToken.None);

    /// <summary>Confirms the user's email if <paramref name="token"/> is valid.</summary>
    public async Task<HuiaIdentityResult> ConfirmEmailAsync(HuiaUser user, string token)
    {
        var valid = await RequireProvider(Options.Tokens.DefaultProvider)
            .ValidateAsync(user, "EmailConfirmation", token, CancellationToken.None).ConfigureAwait(false);
        if (!valid)
        {
            return HuiaIdentityResult.Failed(errorDescriber.InvalidToken());
        }

        user.EmailConfirmed = true;
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Generates a token binding a change to <paramref name="phoneNumber"/> specifically.</summary>
    public Task<string> GenerateChangePhoneNumberTokenAsync(HuiaUser user, string phoneNumber) =>
        RequireProvider(Options.Tokens.DefaultProvider)
            .GenerateAsync(user, $"ChangePhoneNumber:{phoneNumber}", CancellationToken.None);

    /// <summary>Applies a phone number change if <paramref name="token"/> is valid for <paramref name="phoneNumber"/>.</summary>
    public async Task<HuiaIdentityResult> ChangePhoneNumberAsync(HuiaUser user, string phoneNumber, string token)
    {
        var valid = await RequireProvider(Options.Tokens.DefaultProvider)
            .ValidateAsync(user, $"ChangePhoneNumber:{phoneNumber}", token, CancellationToken.None)
            .ConfigureAwait(false);
        if (!valid)
        {
            return HuiaIdentityResult.Failed(errorDescriber.InvalidToken());
        }

        user.PhoneNumber = phoneNumber;
        user.NormalizedPhoneNumber = phoneNumber;
        user.PhoneNumberConfirmed = true;
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Generates a two-factor/OTP token from the named provider (e.g. <see cref="HuiaTokenProviders.Phone"/>).</summary>
    public Task<string> GenerateTwoFactorTokenAsync(HuiaUser user, string tokenProvider) =>
        RequireProvider(tokenProvider).GenerateAsync(user, "TwoFactor", CancellationToken.None);

    /// <summary>Verifies a two-factor/OTP token from the named provider.</summary>
    public Task<bool> VerifyTwoFactorTokenAsync(HuiaUser user, string tokenProvider, string token) =>
        RequireProvider(tokenProvider).ValidateAsync(user, "TwoFactor", token, CancellationToken.None);

    #endregion

    #region Lockout

    /// <summary>Records a failed sign-in attempt, locking the account out if the threshold is reached.</summary>
    public async Task<HuiaIdentityResult> AccessFailedAsync(HuiaUser user)
    {
        user.AccessFailedCount++;
        if (user.AccessFailedCount >= Options.Lockout.MaxFailedAccessAttempts && user.LockoutEnabled)
        {
            user.LockoutEnd = DateTimeOffset.UtcNow + Options.Lockout.DefaultLockoutTimeSpan;
            user.AccessFailedCount = 0;
        }

        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Resets the failed-attempt counter after a successful sign-in.</summary>
    public async Task<HuiaIdentityResult> ResetAccessFailedCountAsync(HuiaUser user)
    {
        if (user.AccessFailedCount == 0)
        {
            return HuiaIdentityResult.Success;
        }

        user.AccessFailedCount = 0;
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Whether the account is currently locked out.</summary>
    public Task<bool> IsLockedOutAsync(HuiaUser user) =>
        Task.FromResult(user.LockoutEnabled && user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow);

    /// <summary>Enables or disables lockout for the account.</summary>
    public async Task<HuiaIdentityResult> SetLockoutEnabledAsync(HuiaUser user, bool enabled)
    {
        user.LockoutEnabled = enabled;
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Sets (or clears, with <see langword="null"/>) the account's lockout end time.</summary>
    public async Task<HuiaIdentityResult> SetLockoutEndDateAsync(HuiaUser user, DateTimeOffset? lockoutEnd)
    {
        user.LockoutEnd = lockoutEnd;
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    #endregion

    #region Roles

    /// <summary>The names of every role <paramref name="user"/> belongs to.</summary>
    public async Task<IList<string>> GetRolesAsync(HuiaUser user) =>
        [.. await RequireRoleStore().GetRolesAsync(user, CancellationToken.None).ConfigureAwait(false)];

    /// <summary>Adds <paramref name="user"/> to the named role.</summary>
    public async Task<HuiaIdentityResult> AddToRoleAsync(HuiaUser user, string role)
    {
        if (await RequireRoleStore().IsInRoleAsync(user, role, CancellationToken.None).ConfigureAwait(false))
        {
            return HuiaIdentityResult.Failed(errorDescriber.UserAlreadyInRole(role));
        }

        await RequireRoleStore().AddToRoleAsync(user, role, CancellationToken.None).ConfigureAwait(false);
        return HuiaIdentityResult.Success;
    }

    /// <summary>Removes <paramref name="user"/> from the named role.</summary>
    public async Task<HuiaIdentityResult> RemoveFromRoleAsync(HuiaUser user, string role)
    {
        if (!await RequireRoleStore().IsInRoleAsync(user, role, CancellationToken.None).ConfigureAwait(false))
        {
            return HuiaIdentityResult.Failed(errorDescriber.UserNotInRole(role));
        }

        await RequireRoleStore().RemoveFromRoleAsync(user, role, CancellationToken.None).ConfigureAwait(false);
        return HuiaIdentityResult.Success;
    }

    /// <summary>Whether <paramref name="user"/> belongs to the named role.</summary>
    public Task<bool> IsInRoleAsync(HuiaUser user, string role) =>
        RequireRoleStore().IsInRoleAsync(user, role, CancellationToken.None);

    /// <summary>Every user belonging to the named role.</summary>
    public async Task<IList<HuiaUser>> GetUsersInRoleAsync(string role) =>
        [.. await RequireRoleStore().GetUsersInRoleAsync(role, CancellationToken.None).ConfigureAwait(false)];

    #endregion

    #region External logins

    /// <summary>Links an external login to <paramref name="user"/>.</summary>
    public async Task<HuiaIdentityResult> AddLoginAsync(HuiaUser user, HuiaUserLoginInfo login)
    {
        var existing = await RequireLoginStore()
            .FindByLoginAsync(login.LoginProvider, login.ProviderKey, CancellationToken.None).ConfigureAwait(false);
        if (existing is not null)
        {
            return HuiaIdentityResult.Failed(errorDescriber.LoginAlreadyAssociated());
        }

        await RequireLoginStore().AddLoginAsync(user, login, CancellationToken.None).ConfigureAwait(false);
        return HuiaIdentityResult.Success;
    }

    /// <summary>Links an external login (from a pending sign-in) to <paramref name="user"/>.</summary>
    public Task<HuiaIdentityResult> AddLoginAsync(HuiaUser user, HuiaExternalLoginInfo info) =>
        AddLoginAsync(user, new HuiaUserLoginInfo(info.LoginProvider, info.ProviderKey, info.ProviderDisplayName));

    /// <summary>Unlinks an external login from <paramref name="user"/>.</summary>
    public async Task<HuiaIdentityResult> RemoveLoginAsync(HuiaUser user, string loginProvider, string providerKey)
    {
        await RequireLoginStore().RemoveLoginAsync(user, loginProvider, providerKey, CancellationToken.None)
            .ConfigureAwait(false);
        return HuiaIdentityResult.Success;
    }

    /// <summary>Every external login linked to <paramref name="user"/>.</summary>
    public async Task<IList<HuiaUserLoginInfo>> GetLoginsAsync(HuiaUser user) =>
        [.. await RequireLoginStore().GetLoginsAsync(user, CancellationToken.None).ConfigureAwait(false)];

    /// <summary>Finds the user linked to the given external login, or <see langword="null"/> if none exists.</summary>
    public Task<HuiaUser?> FindByLoginAsync(string loginProvider, string providerKey) =>
        RequireLoginStore().FindByLoginAsync(loginProvider, providerKey, CancellationToken.None);

    #endregion

    #region Provider-specific authentication tokens (e.g. an external provider's OAuth access/refresh token)

    /// <summary>Stores (or clears, with a <see langword="null"/> <paramref name="value"/>) a token under
    /// (<paramref name="user"/>, <paramref name="loginProvider"/>, <paramref name="name"/>) — e.g. an
    /// external provider's OAuth access/refresh token, set by <see cref="HuiaSignInManager.UpdateExternalAuthenticationTokensAsync"/>.</summary>
    public Task SetAuthenticationTokenAsync(HuiaUser user, string loginProvider, string name, string? value) =>
        RequireTokenStore().SetTokenAsync(user, loginProvider, name, value, CancellationToken.None);

    /// <summary>Retrieves a token stored by <see cref="SetAuthenticationTokenAsync"/>.</summary>
    public Task<string?> GetAuthenticationTokenAsync(HuiaUser user, string loginProvider, string name) =>
        RequireTokenStore().GetTokenAsync(user, loginProvider, name, CancellationToken.None);

    #endregion

    #region Two-factor / authenticator / recovery codes

    /// <summary>Enables or disables 2FA for the account.</summary>
    public async Task<HuiaIdentityResult> SetTwoFactorEnabledAsync(HuiaUser user, bool enabled)
    {
        user.TwoFactorEnabled = enabled;
        return await UpdateAsync(user).ConfigureAwait(false);
    }

    /// <summary>Whether 2FA is enabled for the account.</summary>
    public Task<bool> GetTwoFactorEnabledAsync(HuiaUser user) => Task.FromResult(user.TwoFactorEnabled);

    /// <summary>The user's current TOTP authenticator key (base32), or <see langword="null"/> if none has been
    /// provisioned yet.</summary>
    public Task<string?> GetAuthenticatorKeyAsync(HuiaUser user) =>
        RequireTokenStore().GetTokenAsync(user, HuiaTokenProviders.Authenticator,
            TotpHuiaTokenProvider.AuthenticatorKeyTokenName, CancellationToken.None);

    /// <summary>Provisions (or replaces) the user's TOTP authenticator key, clearing any existing recovery
    /// codes — they were generated against the old key's enrollment and are meaningless once it changes.</summary>
    public async Task ResetAuthenticatorKeyAsync(HuiaUser user)
    {
        var key = Base32.Encode(RandomNumberGenerator.GetBytes(20));
        await RequireTokenStore().SetTokenAsync(user, HuiaTokenProviders.Authenticator,
            TotpHuiaTokenProvider.AuthenticatorKeyTokenName, key, CancellationToken.None).ConfigureAwait(false);
        await RequireTokenStore().SetTokenAsync(user, HuiaTokenProviders.Authenticator, RecoveryCodesTokenName,
            null, CancellationToken.None).ConfigureAwait(false);
    }

    private const string RecoveryCodesTokenName = "RecoveryCodes";
    private const char RecoveryCodeSeparator = ';';

    /// <summary>How many unused recovery codes remain.</summary>
    public async Task<int> CountRecoveryCodesAsync(HuiaUser user)
    {
        var stored = await RequireTokenStore()
            .GetTokenAsync(user, HuiaTokenProviders.Authenticator, RecoveryCodesTokenName, CancellationToken.None)
            .ConfigureAwait(false);
        return string.IsNullOrEmpty(stored) ? 0 : stored.Split(RecoveryCodeSeparator).Length;
    }

    /// <summary>Generates <paramref name="number"/> fresh recovery codes, replacing any existing ones, and
    /// returns the plaintext codes (shown to the user once — only their hashes are persisted).</summary>
    public async Task<IEnumerable<string>?> GenerateNewTwoFactorRecoveryCodesAsync(HuiaUser user, int number)
    {
        var codes = new List<string>(number);
        for (var i = 0; i < number; i++)
        {
            codes.Add(
                $"{RandomNumberGenerator.GetInt32(0, 100_000_000):D8}-" +
                $"{RandomNumberGenerator.GetInt32(0, 100_000_000):D8}");
        }

        var hashed = string.Join(RecoveryCodeSeparator, codes.Select(passwordHasher.HashPassword));
        await RequireTokenStore().SetTokenAsync(user, HuiaTokenProviders.Authenticator, RecoveryCodesTokenName,
            hashed, CancellationToken.None).ConfigureAwait(false);

        logger.LogInformation("Generated new two-factor recovery codes for user {UserId}.", user.Id);
        return codes;
    }

    /// <summary>Redeems (consumes) a recovery code, if it matches an unused one.</summary>
    public async Task<HuiaIdentityResult> RedeemTwoFactorRecoveryCodeAsync(HuiaUser user, string code)
    {
        var stored = await RequireTokenStore()
            .GetTokenAsync(user, HuiaTokenProviders.Authenticator, RecoveryCodesTokenName, CancellationToken.None)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(stored))
        {
            return HuiaIdentityResult.Failed(errorDescriber.RecoveryCodeRedemptionFailed());
        }

        var hashedCodes = stored.Split(RecoveryCodeSeparator);
        var matchIndex = Array.FindIndex(hashedCodes,
            hashed => passwordHasher.VerifyHashedPassword(hashed, code) != HuiaPasswordVerificationResult.Failed);
        if (matchIndex < 0)
        {
            return HuiaIdentityResult.Failed(errorDescriber.RecoveryCodeRedemptionFailed());
        }

        var remaining = hashedCodes.Where((_, i) => i != matchIndex);
        await RequireTokenStore().SetTokenAsync(user, HuiaTokenProviders.Authenticator, RecoveryCodesTokenName,
            string.Join(RecoveryCodeSeparator, remaining), CancellationToken.None).ConfigureAwait(false);

        return HuiaIdentityResult.Success;
    }

    #endregion
}
