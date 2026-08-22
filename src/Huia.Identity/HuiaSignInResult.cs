namespace Huia.Identity;

/// <summary>The result of a sign-in attempt performed by <see cref="HuiaSignInManager"/>. Replaces
/// ASP.NET Core Identity's <c>SignInResult</c>.</summary>
public sealed class HuiaSignInResult
{
    /// <summary>Whether the sign-in succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>Whether the user must complete two-factor authentication before signing in.</summary>
    public bool RequiresTwoFactor { get; }

    /// <summary>Whether the account is currently locked out.</summary>
    public bool IsLockedOut { get; }

    /// <summary>Whether the account isn't allowed to sign in (e.g. email confirmation required and missing).</summary>
    public bool IsNotAllowed { get; }

    private HuiaSignInResult(bool succeeded, bool requiresTwoFactor, bool isLockedOut, bool isNotAllowed)
    {
        Succeeded = succeeded;
        RequiresTwoFactor = requiresTwoFactor;
        IsLockedOut = isLockedOut;
        IsNotAllowed = isNotAllowed;
    }

    /// <summary>A successful sign-in.</summary>
    public static HuiaSignInResult Success { get; } = new(true, false, false, false);

    /// <summary>A failed sign-in (invalid credentials) — not locked out, not requiring 2FA.</summary>
    public static HuiaSignInResult Failed { get; } = new(false, false, false, false);

    /// <summary>The account is locked out.</summary>
    public static HuiaSignInResult LockedOut { get; } = new(false, false, true, false);

    /// <summary>The account isn't allowed to sign in.</summary>
    public static HuiaSignInResult NotAllowed { get; } = new(false, false, false, true);

    /// <summary>Two-factor authentication is required to complete sign-in.</summary>
    public static HuiaSignInResult TwoFactorRequired { get; } = new(false, true, false, false);

    /// <inheritdoc />
    public override string ToString() =>
        IsLockedOut ? "Lockedout"
        : IsNotAllowed ? "NotAllowed"
        : RequiresTwoFactor ? "RequiresTwoFactor"
        : Succeeded ? "Succeeded"
        : "Failed";
}
