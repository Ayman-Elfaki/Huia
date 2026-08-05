namespace Huia.Keys;

/// <summary>
/// Configures Huia's automatic signing/encryption key rotation: a new key is generated ahead of the current
/// one's retirement so there is never a gap, and retired keys stay valid for validating already-issued
/// tokens for a grace period before being purged.
/// </summary>
public sealed class KeyManagementOptions
{
    /// <summary>How long a key actively signs new tokens before being retired. Default: 90 days.</summary>
    public TimeSpan ActiveLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// How far ahead of a key's retirement the next key is generated, so it has propagated before it's
    /// needed. Default: 14 days.
    /// </summary>
    public TimeSpan RotationLeadTime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How much longer a retired key stays valid for validating tokens it already issued. Should be at
    /// least as long as your longest-lived token (typically the refresh token). Default: 30 days.
    /// </summary>
    public TimeSpan ValidationOverlap { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Quartz cron expression for the rotation check. Default: daily at 03:00.</summary>
    public string RotationCronExpression { get; set; } = "0 0 3 * * ?";

    /// <summary>RSA key size in bits for newly generated keys. Default: 2048.</summary>
    public int RsaKeySizeInBits { get; set; } = 2048;

    /// <summary>
    /// RSA signing algorithm for the signing key (has no effect on the encryption key, which always uses
    /// RSA-OAEP/A256CBC-HS512). Default: <see cref="Keys.SigningAlgorithm.RS256"/>.
    /// </summary>
    public SigningAlgorithm SigningAlgorithm { get; set; } = SigningAlgorithm.RS256;
}