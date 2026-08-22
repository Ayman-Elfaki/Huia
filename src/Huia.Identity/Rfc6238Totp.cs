using System.Security.Cryptography;
using System.Text;

namespace Huia.Identity;

/// <summary>
/// The RFC 4226 (HOTP)/RFC 6238 (TOTP) dynamic-truncation algorithm, shared by
/// <see cref="PhoneOtpHuiaTokenProvider"/> (security-stamp-keyed, configurable step) and
/// <see cref="TotpHuiaTokenProvider"/> (authenticator-key-keyed, fixed 30-second step) — the same standardized
/// construction ASP.NET Core Identity's own token providers use internally, reimplemented here against
/// <see cref="HMACSHA1"/> directly so neither depends on <c>Microsoft.AspNetCore.Identity</c>.
/// </summary>
internal static class Rfc6238Totp
{
    /// <summary>
    /// Computes a 6-digit code from <paramref name="securityToken"/> (the HMAC key) and
    /// <paramref name="timestepNumber"/> (the moving factor), optionally folding in
    /// <paramref name="modifier"/> so different purposes/users never collide even when they'd otherwise share
    /// a timestep.
    /// </summary>
    public static int ComputeCode(byte[] securityToken, long timestepNumber, string? modifier)
    {
        Span<byte> timestepAsBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(timestepAsBytes, timestepNumber);

        var message = ApplyModifier(timestepAsBytes, modifier);

        using var hmac = new HMACSHA1(securityToken);
        var hash = hmac.ComputeHash(message);

        // Dynamic truncation (RFC 4226 §5.3): use the low nibble of the last byte as an offset into the HMAC
        // output, then mask off the top bit of the resulting 4-byte window to avoid sign ambiguity.
        var offset = hash[^1] & 0xf;
        var binaryCode = (hash[offset] & 0x7f) << 24
                          | (hash[offset + 1] & 0xff) << 16
                          | (hash[offset + 2] & 0xff) << 8
                          | (hash[offset + 3] & 0xff);

        return binaryCode % 1_000_000;
    }

    private static byte[] ApplyModifier(ReadOnlySpan<byte> input, string? modifier)
    {
        if (string.IsNullOrEmpty(modifier))
        {
            return input.ToArray();
        }

        var modifierBytes = Encoding.UTF8.GetBytes(modifier);
        var combined = new byte[input.Length + modifierBytes.Length];
        input.CopyTo(combined);
        modifierBytes.CopyTo(combined, input.Length);
        return combined;
    }

    /// <summary>Constant-time comparison of two 6-digit codes, avoiding a timing side channel on the digit
    /// values themselves.</summary>
    public static bool CodesEqual(int expected, int actual)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected.ToString("D6", System.Globalization.CultureInfo.InvariantCulture));
        var actualBytes = Encoding.ASCII.GetBytes(actual.ToString("D6", System.Globalization.CultureInfo.InvariantCulture));
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
