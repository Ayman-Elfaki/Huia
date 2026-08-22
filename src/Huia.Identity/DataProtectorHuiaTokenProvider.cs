using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Huia.Identity;

/// <summary>
/// The <see cref="HuiaTokenProviders.Default"/> provider — password reset, email confirmation, and
/// change-phone-number tokens. Ports ASP.NET Core Identity's own <c>DataProtectorTokenProvider</c> algorithm:
/// <see cref="IDataProtector.Protect(byte[])"/> over (creation time, user id, purpose, security stamp),
/// base64url-encoded. Validating unprotects and checks the embedded security stamp still matches the user's
/// current one — this is what makes a reset/confirmation link auto-invalidate after a password change (or any
/// other security-stamp-bumping event) — and that the token hasn't outlived
/// <see cref="HuiaTokenOptions.DefaultTokenLifetime"/>.
/// </summary>
/// <remarks>
/// Security review flag: the token's expiry and security-stamp binding are the only integrity checks here —
/// <see cref="IDataProtector"/> itself guarantees the payload can't be forged or tampered with (it's
/// authenticated encryption), so this class only needs to check the fields it embedded. Worth a
/// <c>/security-review</c> pass to confirm that reasoning before this ships.
/// </remarks>
public sealed class DataProtectorHuiaTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<HuiaIdentityOptions> options) : IHuiaTokenProvider
{
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector("Huia.Identity.DataProtectorHuiaTokenProvider");

    /// <inheritdoc />
    public string Name => HuiaTokenProviders.Default;

    /// <inheritdoc />
    public Task<string> GenerateAsync(HuiaUser user, string purpose, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(DateTimeOffset.UtcNow.UtcTicks);
            writer.Write(user.Id);
            writer.Write(purpose);
            writer.Write(user.SecurityStamp);
        }

        var protectedBytes = _protector.Protect(stream.ToArray());
        return Task.FromResult(WebEncoders.Base64UrlEncode(protectedBytes));
    }

    /// <inheritdoc />
    public Task<bool> ValidateAsync(HuiaUser user, string purpose, string token, CancellationToken cancellationToken)
    {
        try
        {
            var unprotectedBytes = _protector.Unprotect(WebEncoders.Base64UrlDecode(token));
            using var stream = new MemoryStream(unprotectedBytes);
            using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);

            var creationTime = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var expirationTime = creationTime + options.Value.Tokens.DefaultTokenLifetime;
            if (expirationTime < DateTimeOffset.UtcNow)
            {
                return Task.FromResult(false);
            }

            var userId = reader.ReadString();
            if (!string.Equals(userId, user.Id, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            var tokenPurpose = reader.ReadString();
            if (!string.Equals(tokenPurpose, purpose, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            var stamp = reader.ReadString();
            var securityStampMatches = stamp.Length == 0 ||
                CryptographicOperations.FixedTimeEquals(Encoding.Unicode.GetBytes(stamp),
                    Encoding.Unicode.GetBytes(user.SecurityStamp));

            // Nothing should remain unread — a mismatched format (e.g. a token from an unrelated purpose that
            // happened to decrypt) would otherwise leave trailing bytes unnoticed.
            if (stream.Position != stream.Length)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(securityStampMatches);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or EndOfStreamException)
        {
            return Task.FromResult(false);
        }
    }
}
