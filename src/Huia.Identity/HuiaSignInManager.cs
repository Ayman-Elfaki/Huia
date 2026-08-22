using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Huia.Identity;

/// <summary>
/// Huia's own sign-in manager — replaces ASP.NET Core Identity's <c>SignInManager&lt;HuiaUser&gt;</c>, kept
/// name/shape-compatible with it so existing call sites migrate by swapping the type. Owns cookie sign-in/out
/// against <see cref="HuiaAuthenticationDefaults"/>'s schemes, password/2FA/external-login sign-in flows, and
/// the "remember this machine" 2FA-skip cookie.
/// </summary>
public class HuiaSignInManager(
    HuiaUserManager userManager,
    IHttpContextAccessor httpContextAccessor,
    IAuthenticationSchemeProvider schemeProvider,
    ILogger<HuiaSignInManager> logger)
{
    /// <summary>The claim type the sign-in cookie's security-stamp claim is stored under — checked by
    /// <see cref="HuiaOptions"/>'s <c>OnValidatePrincipal</c> revalidation handler.</summary>
    public const string SecurityStampClaimType = "Huia.SecurityStamp";

    private const string LoginProviderKey = "LoginProvider";
    private const string XsrfKey = "XsrfId";

    private HttpContext Context => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("HttpContext is not available.");

    #region Cookie sign-in / sign-out

    /// <summary>Builds the claims principal for a signed-in user's <see cref="HuiaAuthenticationDefaults.ApplicationScheme"/>
    /// cookie.</summary>
    public Task<ClaimsPrincipal> CreateUserPrincipalAsync(HuiaUser user)
    {
        var identity = new ClaimsIdentity(HuiaAuthenticationDefaults.ApplicationScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        if (!string.IsNullOrEmpty(user.UserName))
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
        }

        identity.AddClaim(new Claim(SecurityStampClaimType, user.SecurityStamp));
        return Task.FromResult(new ClaimsPrincipal(identity));
    }

    /// <summary>Signs <paramref name="user"/> into the application cookie.</summary>
    public async Task SignInAsync(HuiaUser user, bool isPersistent)
    {
        var principal = await CreateUserPrincipalAsync(user).ConfigureAwait(false);
        var properties = new AuthenticationProperties { IsPersistent = isPersistent };
        await Context.SignInAsync(HuiaAuthenticationDefaults.ApplicationScheme, principal, properties)
            .ConfigureAwait(false);
    }

    /// <summary>Clears the application cookie and any pending external/2FA cookies.</summary>
    public async Task SignOutAsync()
    {
        await Context.SignOutAsync(HuiaAuthenticationDefaults.ApplicationScheme).ConfigureAwait(false);
        await Context.SignOutAsync(HuiaAuthenticationDefaults.ExternalScheme).ConfigureAwait(false);
        await Context.SignOutAsync(HuiaAuthenticationDefaults.TwoFactorUserIdScheme).ConfigureAwait(false);
    }

    /// <summary>Re-issues the application cookie for <paramref name="user"/>, preserving the current cookie's
    /// persistence — e.g. after a profile change that should be reflected without forcing a fresh sign-in.</summary>
    public async Task RefreshSignInAsync(HuiaUser user)
    {
        var current = await Context.AuthenticateAsync(HuiaAuthenticationDefaults.ApplicationScheme)
            .ConfigureAwait(false);
        await SignInAsync(user, current.Properties?.IsPersistent ?? false).ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="user"/> is currently allowed to sign in (not locked out, and — if
    /// <see cref="HuiaSignInOptions.RequireConfirmedAccount"/> is set — has a confirmed email or phone).</summary>
    public Task<bool> CanSignInAsync(HuiaUser user)
    {
        if (userManager.Options.SignIn.RequireConfirmedAccount && !user.EmailConfirmed && !user.PhoneNumberConfirmed)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private async Task<HuiaSignInResult?> PreSignInCheckAsync(HuiaUser user)
    {
        if (!await CanSignInAsync(user).ConfigureAwait(false))
        {
            return HuiaSignInResult.NotAllowed;
        }

        if (await userManager.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            return HuiaSignInResult.LockedOut;
        }

        return null;
    }

    private async Task<HuiaSignInResult> SignInOrTwoFactorAsync(HuiaUser user, bool isPersistent)
    {
        if (user.TwoFactorEnabled && !await IsTwoFactorClientRememberedAsync(user).ConfigureAwait(false))
        {
            await StoreTwoFactorUserIdAsync(user).ConfigureAwait(false);
            return HuiaSignInResult.TwoFactorRequired;
        }

        await SignInAsync(user, isPersistent).ConfigureAwait(false);
        return HuiaSignInResult.Success;
    }

    #endregion

    #region Password sign-in

    /// <summary>Signs in by username/email + password.</summary>
    public async Task<HuiaSignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent,
        bool lockoutOnFailure)
    {
        var user = await userManager.FindByNameAsync(userName).ConfigureAwait(false);
        if (user is null)
        {
            return HuiaSignInResult.Failed;
        }

        return await PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure).ConfigureAwait(false);
    }

    /// <summary>Signs in an already-resolved user by password.</summary>
    public async Task<HuiaSignInResult> PasswordSignInAsync(HuiaUser user, string password, bool isPersistent,
        bool lockoutOnFailure)
    {
        var preCheck = await PreSignInCheckAsync(user).ConfigureAwait(false);
        if (preCheck is not null)
        {
            return preCheck;
        }

        if (!await userManager.VerifyPasswordAsync(user, password).ConfigureAwait(false))
        {
            if (lockoutOnFailure)
            {
                await userManager.AccessFailedAsync(user).ConfigureAwait(false);
                if (await userManager.IsLockedOutAsync(user).ConfigureAwait(false))
                {
                    logger.LogInformation("User {UserId} is locked out.", user.Id);
                    return HuiaSignInResult.LockedOut;
                }
            }

            return HuiaSignInResult.Failed;
        }

        await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        return await SignInOrTwoFactorAsync(user, isPersistent).ConfigureAwait(false);
    }

    #endregion

    #region Two-factor sign-in

    private async Task StoreTwoFactorUserIdAsync(HuiaUser user)
    {
        var identity = new ClaimsIdentity(HuiaAuthenticationDefaults.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        await Context.SignInAsync(HuiaAuthenticationDefaults.TwoFactorUserIdScheme, new ClaimsPrincipal(identity))
            .ConfigureAwait(false);
    }

    /// <summary>The user for a pending two-factor sign-in, or <see langword="null"/> if none is pending.</summary>
    public async Task<HuiaUser?> GetTwoFactorAuthenticationUserAsync()
    {
        var result = await Context.AuthenticateAsync(HuiaAuthenticationDefaults.TwoFactorUserIdScheme)
            .ConfigureAwait(false);
        var userId = result?.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? null : await userManager.FindByIdAsync(userId).ConfigureAwait(false);
    }

    /// <summary>Completes a pending two-factor sign-in with a TOTP authenticator code.</summary>
    public async Task<HuiaSignInResult> TwoFactorAuthenticatorSignInAsync(string code, bool isPersistent,
        bool rememberClient)
    {
        var user = await GetTwoFactorAuthenticationUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return HuiaSignInResult.Failed;
        }

        var valid = await userManager
            .VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, code)
            .ConfigureAwait(false);
        if (!valid)
        {
            await userManager.AccessFailedAsync(user).ConfigureAwait(false);
            if (await userManager.IsLockedOutAsync(user).ConfigureAwait(false))
            {
                return HuiaSignInResult.LockedOut;
            }

            return HuiaSignInResult.Failed;
        }

        await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        await Context.SignOutAsync(HuiaAuthenticationDefaults.TwoFactorUserIdScheme).ConfigureAwait(false);

        if (rememberClient)
        {
            await RememberTwoFactorClientAsync(user).ConfigureAwait(false);
        }

        await SignInAsync(user, isPersistent).ConfigureAwait(false);
        return HuiaSignInResult.Success;
    }

    /// <summary>Completes a pending two-factor sign-in by redeeming a recovery code.</summary>
    public async Task<HuiaSignInResult> TwoFactorRecoveryCodeSignInAsync(string code)
    {
        var user = await GetTwoFactorAuthenticationUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return HuiaSignInResult.Failed;
        }

        var result = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return HuiaSignInResult.Failed;
        }

        await Context.SignOutAsync(HuiaAuthenticationDefaults.TwoFactorUserIdScheme).ConfigureAwait(false);
        await SignInAsync(user, isPersistent: false).ConfigureAwait(false);
        return HuiaSignInResult.Success;
    }

    private async Task RememberTwoFactorClientAsync(HuiaUser user)
    {
        var identity = new ClaimsIdentity(HuiaAuthenticationDefaults.TwoFactorRememberMeScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        identity.AddClaim(new Claim(SecurityStampClaimType, user.SecurityStamp));
        var properties = new AuthenticationProperties { IsPersistent = true };
        await Context.SignInAsync(HuiaAuthenticationDefaults.TwoFactorRememberMeScheme,
            new ClaimsPrincipal(identity), properties).ConfigureAwait(false);
    }

    private async Task<bool> IsTwoFactorClientRememberedAsync(HuiaUser user)
    {
        var result = await Context.AuthenticateAsync(HuiaAuthenticationDefaults.TwoFactorRememberMeScheme)
            .ConfigureAwait(false);
        if (result?.Principal is null)
        {
            return false;
        }

        var userId = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = result.Principal.FindFirstValue(SecurityStampClaimType);
        return string.Equals(userId, user.Id, StringComparison.Ordinal) &&
               string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal);
    }

    #endregion

    #region External login

    /// <summary>Builds the properties a challenge to <paramref name="provider"/> carries through the round
    /// trip — <paramref name="redirectUrl"/> is where the callback resumes; <paramref name="userId"/>, when
    /// linking a provider to an already-signed-in account, is checked again by
    /// <see cref="GetExternalLoginInfoAsync(string?)"/> to stop a forged callback from linking to a different
    /// account than the one that started the challenge.</summary>
    public AuthenticationProperties ConfigureExternalAuthenticationProperties(string provider, string? redirectUrl,
        string? userId = null)
    {
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        properties.Items[LoginProviderKey] = provider;
        if (userId is not null)
        {
            properties.Items[XsrfKey] = userId;
        }

        return properties;
    }

    /// <summary>Retrieves the pending external login from <see cref="HuiaAuthenticationDefaults.ExternalScheme"/>,
    /// or <see langword="null"/> if none is pending (or, when <paramref name="expectedXsrfKey"/> is supplied,
    /// if it doesn't match the id recorded when the challenge started).</summary>
    public async Task<HuiaExternalLoginInfo?> GetExternalLoginInfoAsync(string? expectedXsrfKey = null)
    {
        var result = await Context.AuthenticateAsync(HuiaAuthenticationDefaults.ExternalScheme).ConfigureAwait(false);
        var items = result?.Properties?.Items;
        if (result?.Principal is null || items is null ||
            !items.TryGetValue(LoginProviderKey, out var provider) || provider is null)
        {
            return null;
        }

        if (expectedXsrfKey is not null &&
            (!items.TryGetValue(XsrfKey, out var storedUserId) ||
             !string.Equals(storedUserId, expectedXsrfKey, StringComparison.Ordinal)))
        {
            return null;
        }

        var providerKey = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (providerKey is null)
        {
            return null;
        }

        var schemes = await schemeProvider.GetAllSchemesAsync().ConfigureAwait(false);
        var displayName = schemes
            .FirstOrDefault(s => string.Equals(s.Name, provider, StringComparison.Ordinal))?.DisplayName;

        return new HuiaExternalLoginInfo
        {
            LoginProvider = provider,
            ProviderKey = providerKey,
            ProviderDisplayName = displayName,
            Principal = result.Principal,
            AuthenticationProperties = result.Properties,
        };
    }

    /// <summary>Every registered scheme with a display name — Huia's own cookie schemes don't set one, so
    /// this naturally returns only external (third-party) providers, for rendering one sign-in button each.</summary>
    public async Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemesAsync()
    {
        var schemes = await schemeProvider.GetAllSchemesAsync().ConfigureAwait(false);
        return schemes.Where(s => !string.IsNullOrEmpty(s.DisplayName));
    }

    /// <summary>Signs in the user linked to the given external login.</summary>
    public async Task<HuiaSignInResult> ExternalLoginSignInAsync(string loginProvider, string providerKey,
        bool isPersistent, bool bypassTwoFactor = false)
    {
        var user = await userManager.FindByLoginAsync(loginProvider, providerKey).ConfigureAwait(false);
        if (user is null)
        {
            return HuiaSignInResult.Failed;
        }

        var preCheck = await PreSignInCheckAsync(user).ConfigureAwait(false);
        if (preCheck is not null)
        {
            return preCheck;
        }

        if (!bypassTwoFactor && user.TwoFactorEnabled && !await IsTwoFactorClientRememberedAsync(user).ConfigureAwait(false))
        {
            await StoreTwoFactorUserIdAsync(user).ConfigureAwait(false);
            return HuiaSignInResult.TwoFactorRequired;
        }

        await SignInAsync(user, isPersistent).ConfigureAwait(false);
        return HuiaSignInResult.Success;
    }

    /// <summary>Persists whatever access/refresh tokens the provider returned (<see cref="AuthenticationProperties.GetTokens"/>)
    /// against the user linked to <paramref name="info"/>.</summary>
    public async Task UpdateExternalAuthenticationTokensAsync(HuiaExternalLoginInfo info)
    {
        var tokens = info.AuthenticationProperties?.GetTokens();
        if (tokens is null)
        {
            return;
        }

        var user = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ConfigureAwait(false);
        if (user is null)
        {
            return;
        }

        foreach (var token in tokens)
        {
            await userManager.SetAuthenticationTokenAsync(user, info.LoginProvider, token.Name, token.Value)
                .ConfigureAwait(false);
        }
    }

    #endregion
}
