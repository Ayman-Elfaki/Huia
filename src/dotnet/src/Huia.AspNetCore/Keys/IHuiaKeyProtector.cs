using Microsoft.AspNetCore.DataProtection;

namespace Huia.AspNetCore.Keys;

/// <summary>Wraps and unwraps signing-key private material with ASP.NET Core Data Protection.</summary>
public interface IHuiaKeyProtector
{
    /// <summary>Wraps a PKCS#8 private key, returning a Base64 payload safe to persist.</summary>
    /// <param name="privateKeyPkcs8">The DER-encoded PKCS#8 private key.</param>
    /// <returns>The wrapped key.</returns>
    string Protect(byte[] privateKeyPkcs8);

    /// <summary>Unwraps a payload previously produced by <see cref="Protect"/>.</summary>
    /// <param name="wrapped">The wrapped key.</param>
    /// <returns>The DER-encoded PKCS#8 private key.</returns>
    byte[] Unprotect(string wrapped);
}

/// <summary>Data Protection-backed <see cref="IHuiaKeyProtector"/> with a dedicated, versioned purpose.</summary>
internal sealed class DataProtectionKeyProtector : IHuiaKeyProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionKeyProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("Huia.SigningKeys.v1");
    }

    public string Protect(byte[] privateKeyPkcs8)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPkcs8);
        return Convert.ToBase64String(_protector.Protect(privateKeyPkcs8));
    }

    public byte[] Unprotect(string wrapped)
    {
        ArgumentException.ThrowIfNullOrEmpty(wrapped);
        return _protector.Unprotect(Convert.FromBase64String(wrapped));
    }
}
