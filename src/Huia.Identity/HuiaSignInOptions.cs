namespace Huia.Identity;

/// <summary>Sign-in policy. Mirrors ASP.NET Core Identity's <c>SignInOptions</c>.</summary>
public sealed class HuiaSignInOptions
{
    /// <summary>Whether an account must have a confirmed email or phone number before it can sign in.
    /// Defaults to <see langword="false"/> (set explicitly by <c>AddHuia</c>).</summary>
    public bool RequireConfirmedAccount { get; set; }
}
