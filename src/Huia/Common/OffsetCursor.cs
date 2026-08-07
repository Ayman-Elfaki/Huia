using System.Globalization;
using System.Text;

namespace Huia.Common;

/// <summary>
/// Encodes/decodes an admin list endpoint's pagination cursor. The cursor is opaque to callers (a base64
/// string, not a page number) so the API surface reads as forward-only cursor pagination, but is backed
/// internally by a plain integer offset into whatever stable order the endpoint already queries in — true
/// keyset pagination isn't safe here without either hard-coupling admin endpoints to a concrete EF Core
/// entity type (breaking <c>IHuiaStore</c>'s store-agnostic design) or relying on EF Core's unreliable
/// string-comparison SQL translation, since every one of these lists is ordered by a string column
/// (an id, email, or name).
/// </summary>
internal static class OffsetCursor
{
    public static string Encode(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Returns 0 for a missing, malformed, or tampered cursor rather than throwing - this is
    /// client-supplied input, and worst case an invalid cursor just restarts pagination from the top.</summary>
    public static int Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset >= 0
                ? offset
                : 0;
        }
        catch (FormatException)
        {
            return 0;
        }
    }
}
