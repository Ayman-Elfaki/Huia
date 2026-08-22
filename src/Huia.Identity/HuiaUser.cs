namespace Huia.Identity;

/// <summary>
/// Huia's own user type — no longer inherits ASP.NET Core Identity's <c>IdentityUser</c>; every column is
/// owned directly. Inherit from this to add custom claims.
/// </summary>
public class HuiaUser
{
    /// <summary>The user's unique id.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The user's username — for a password account, this is the email address; for a passwordless
    /// phone account, the normalized phone number.</summary>
    public string? UserName { get; set; }

    /// <summary><see cref="UserName"/>, normalized (upper-invariant) for case-insensitive lookup.</summary>
    public string? NormalizedUserName { get; set; }

    /// <summary>The user's email address.</summary>
    public string? Email { get; set; }

    /// <summary><see cref="Email"/>, normalized (upper-invariant) for case-insensitive lookup.</summary>
    public string? NormalizedEmail { get; set; }

    /// <summary>Whether <see cref="Email"/> has been confirmed (link click or provider-verified claim).</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>The PBKDF2-hashed password, or <see langword="null"/> for a passwordless-only/external-only
    /// account. See <see cref="Huia.Identity.Pbkdf2PasswordHasher"/>.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>The user's phone number, in whatever form it was supplied/confirmed.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// <see cref="PhoneNumber"/> normalized to E.164 (e.g. <c>+15551234567</c>) by
    /// <c>Huia.Common.PhoneNumberValidator</c>, used to look up an account by phone number
    /// (<see cref="Huia.Stores.IHuiaPhoneNumberStore"/>). Denormalized and populated by application code —
    /// not computed by the database.
    /// </summary>
    public string? NormalizedPhoneNumber { get; set; }

    /// <summary>Whether <see cref="PhoneNumber"/> has been confirmed (OTP verification).</summary>
    public bool PhoneNumberConfirmed { get; set; }

    /// <summary>
    /// Whether <see cref="PhoneNumber"/> alone (proven via OTP) is sufficient to sign into this account.
    /// <c>false</c> by default even when <see cref="PhoneNumber"/> is set — e.g. a number recorded for a
    /// future SMS-2FA feature or by an admin doesn't implicitly become a standalone sign-in path. Only
    /// accounts created through <c>huia.Authentication.UsePasswordlessFlow()</c>'s phone sign-in flow set
    /// this to <c>true</c>. See docs/passwordless.md's hybrid-auth security considerations for why this
    /// isn't inferred from <see cref="PhoneNumber"/>/<see cref="PhoneNumberConfirmed"/> alone.
    /// </summary>
    public bool PasswordlessLoginEnabled { get; set; }

    /// <summary>Whether two-factor authentication (TOTP) is enabled for this account.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Whether lockout is enabled for this account.</summary>
    public bool LockoutEnabled { get; set; }

    /// <summary>When set and in the future, the account is locked out until this time.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>Consecutive failed sign-in attempts since the last success/reset.</summary>
    public int AccessFailedCount { get; set; }

    /// <summary>
    /// Regenerated whenever a security-relevant change happens (password change, external login added/removed,
    /// 2FA toggled, etc.) — embedded in the sign-in cookie and re-checked periodically
    /// (<see cref="Huia.HuiaOptions"/>'s <c>OnValidatePrincipal</c> handler) and in password-reset/email-
    /// confirmation tokens, so either invalidates outstanding sessions/tokens the moment it changes.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Optimistic-concurrency token, regenerated on every update.</summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The user's given (first) name.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// The user's family (last) name.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// A URL to the user's avatar/profile picture — populated from an external sign-in provider's
    /// <c>picture</c> claim when the account was created that way (see
    /// <c>ExternalLoginConfirmationModel</c>); never set for a password-registered account. Not validated or
    /// re-fetched by Huia itself — treat it as untrusted, provider-supplied display data.
    /// </summary>
    public string? Picture { get; set; }

    /// <inheritdoc />
    public override string ToString() => UserName ?? string.Empty;
}
