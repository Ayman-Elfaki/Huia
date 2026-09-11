using System.Security.Cryptography;
using System.Text;

namespace Huia.Services;

/// <summary>
/// One-time code hashing shared by <see cref="OtpService"/> (accounts) and the pending-signup store
/// (numbers with no account). The stored form is <c>SHA-256(salt ‖ code)</c>; comparison is
/// constant-time.
/// </summary>
internal static class OtpHashing
{
    /// <summary>Hashes a code with a fresh random salt.</summary>
    /// <param name="code">The plain code.</param>
    /// <returns>The Base64 hash and salt.</returns>
    public static (string Hash, string Salt) Create(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        var salt = RandomNumberGenerator.GetBytes(16);
        return (Convert.ToBase64String(Hash(salt, code)), Convert.ToBase64String(salt));
    }

    /// <summary>Constant-time check of a candidate code against a stored hash and salt.</summary>
    /// <param name="code">The candidate code.</param>
    /// <param name="hash">The stored Base64 hash.</param>
    /// <param name="salt">The stored Base64 salt.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    public static bool Verify(string code, string hash, string salt)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
        {
            return false;
        }

        byte[] expected;
        byte[] actual;
        try
        {
            expected = Convert.FromBase64String(hash);
            actual = Hash(Convert.FromBase64String(salt), code);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] Hash(byte[] salt, string code)
    {
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var buffer = new byte[salt.Length + codeBytes.Length];
        salt.CopyTo(buffer, 0);
        codeBytes.CopyTo(buffer, salt.Length);
        return SHA256.HashData(buffer);
    }
}
