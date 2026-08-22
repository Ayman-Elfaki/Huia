namespace Huia.Identity;

/// <summary>
/// Hashes and verifies passwords. Replaces ASP.NET Core Identity's <c>IPasswordHasher&lt;TUser&gt;</c> — see
/// <see cref="Pbkdf2PasswordHasher"/> for the built-in implementation.
/// </summary>
public interface IHuiaPasswordHasher
{
    /// <summary>Hashes <paramref name="password"/> into a self-describing, storable string.</summary>
    string HashPassword(string password);

    /// <summary>Verifies <paramref name="password"/> against <paramref name="hashedPassword"/> (produced by
    /// <see cref="HashPassword"/>), in constant time.</summary>
    HuiaPasswordVerificationResult VerifyHashedPassword(string hashedPassword, string password);
}
