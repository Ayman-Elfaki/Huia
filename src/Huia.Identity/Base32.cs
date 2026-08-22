using System.Text;

namespace Huia.Identity;

/// <summary>RFC 4648 base32 encoding, used for <see cref="TotpHuiaTokenProvider"/>'s <c>AuthenticatorKey</c> —
/// the conventional text form for a TOTP shared secret (what authenticator apps expect when a key is typed in
/// or embedded in an <c>otpauth://</c> QR code).</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        var bitBuffer = 0;
        var bitsInBuffer = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                builder.Append(Alphabet[(bitBuffer >> bitsInBuffer) & 0x1f]);
            }
        }

        if (bitsInBuffer > 0)
        {
            builder.Append(Alphabet[(bitBuffer << (5 - bitsInBuffer)) & 0x1f]);
        }

        return builder.ToString();
    }

    public static byte[] Decode(string base32)
    {
        ArgumentNullException.ThrowIfNull(base32);

        var trimmed = base32.TrimEnd('=');
        var bytes = new List<byte>((trimmed.Length * 5) / 8);
        var bitBuffer = 0;
        var bitsInBuffer = 0;

        foreach (var c in trimmed)
        {
            var value = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (value < 0)
            {
                continue;
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((bitBuffer >> bitsInBuffer) & 0xff));
            }
        }

        return [.. bytes];
    }
}
