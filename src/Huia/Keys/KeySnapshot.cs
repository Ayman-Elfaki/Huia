namespace Huia.Keys;

/// <summary>
/// An immutable, in-memory view of the currently valid keys, ordered so index 0 is always the key that
/// should sign/encrypt new tokens (the newest non-retired key), followed by older keys kept around only to
/// validate tokens they already issued.
/// </summary>
public sealed record KeySnapshot(IReadOnlyList<KeyDescriptor> SigningKeys, IReadOnlyList<KeyDescriptor> EncryptionKeys)
{
    /// <summary>A snapshot with no keys at all, used before the first key rotation has run.</summary>
    public static readonly KeySnapshot Empty = new([], []);
}