namespace Huia.Keys;

/// <summary>What an automatically-managed Huia key is used for.</summary>
public enum KeyUsage
{
    /// <summary>Signs access/identity tokens (OpenIddict signing credentials).</summary>
    Signing,

    /// <summary>Encrypts tokens (OpenIddict encryption credentials).</summary>
    Encryption,
}