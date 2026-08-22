using System.Globalization;
using System.Security.Cryptography;

namespace Huia.Identity;

/// <summary>
/// Hashes passwords with PBKDF2-HMACSHA256 using only BCL cryptography
/// (<see cref="Rfc2898DeriveBytes"/>/<see cref="HMACSHA256"/>) — no dependency on
/// <c>Microsoft.AspNetCore.Identity</c> or <c>Microsoft.AspNetCore.Cryptography.KeyDerivation</c>.
/// </summary>
/// <remarks>
/// Encodes each hash as a self-describing string — <c>PBKDF2-SHA256.&lt;iterations&gt;.&lt;saltB64&gt;.&lt;hashB64&gt;</c>
/// — so a future parameter bump (iteration count) is both backward-compatible (an old hash still verifies
/// against its own recorded iteration count) and forward-detectable (<see cref="VerifyHashedPassword"/>
/// reports <see cref="HuiaPasswordVerificationResult.SuccessRehashNeeded"/> whenever a verified hash's
/// iteration count is lower than <see cref="Iterations"/>, so callers can transparently rehash and
/// re-persist on next successful sign-in) — the same rehash-on-verify behavior ASP.NET Core Identity's own
/// <c>PasswordHasher&lt;TUser&gt;</c> provides.
///
/// Security review flag: PBKDF2 parameters (210,000 iterations, HMACSHA256, 128-bit salt, 256-bit derived
/// key) reflect OWASP's 2023 guidance at the time this was written but should be re-validated by a
/// <c>/security-review</c> pass before this ships, since recommended iteration counts increase over time.
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IHuiaPasswordHasher
{
    private const string Prefix = "PBKDF2-SHA256";
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;

    /// <summary>PBKDF2 iteration count used for newly hashed passwords.</summary>
    public int Iterations { get; init; } = 210_000;

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

        return $"{Prefix}.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    /// <inheritdoc />
    public HuiaPasswordVerificationResult VerifyHashedPassword(string hashedPassword, string password)
    {
        ArgumentNullException.ThrowIfNull(hashedPassword);
        ArgumentNullException.ThrowIfNull(password);

        var parts = hashedPassword.Split('.');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return HuiaPasswordVerificationResult.Failed;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
            iterations <= 0)
        {
            return HuiaPasswordVerificationResult.Failed;
        }

        byte[] salt;
        byte[] expectedKey;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedKey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return HuiaPasswordVerificationResult.Failed;
        }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256,
            expectedKey.Length);

        if (!CryptographicOperations.FixedTimeEquals(actualKey, expectedKey))
        {
            return HuiaPasswordVerificationResult.Failed;
        }

        return iterations < Iterations
            ? HuiaPasswordVerificationResult.SuccessRehashNeeded
            : HuiaPasswordVerificationResult.Success;
    }
}
