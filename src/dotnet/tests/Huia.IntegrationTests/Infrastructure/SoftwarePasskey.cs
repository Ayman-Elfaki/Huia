using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Huia.IntegrationTests.Infrastructure;

/// <summary>
/// A minimal software WebAuthn authenticator for driving the passkey endpoints end to end: an ES256
/// (P-256) key pair that produces the attestation ("none" format) and assertion payloads the ASP.NET
/// Core Identity passkey handler verifies. One instance is one registered credential.
/// </summary>
public sealed class SoftwarePasskey
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private uint _signCount;

    /// <summary>Creates an authenticator bound to <paramref name="origin"/> (also the source of the relying-party id).</summary>
    /// <param name="origin">The request origin, for example <c>http://localhost</c>.</param>
    public SoftwarePasskey(string origin)
    {
        Origin = origin.TrimEnd('/');
        RpId = new Uri(Origin).Host;
    }

    /// <summary>The random 32-byte credential id.</summary>
    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>The base64url credential id, as the management API returns it.</summary>
    public string CredentialIdB64 => WebEncoders.Base64UrlEncode(CredentialId);

    /// <summary>The origin ceremonies are performed from.</summary>
    public string Origin { get; }

    /// <summary>The relying-party id (host of <see cref="Origin"/>).</summary>
    public string RpId { get; }

    /// <summary>Produces the attestation credential JSON for a <c>navigator.credentials.create</c> equivalent.</summary>
    /// <param name="creationOptionsJson">The server's creation-options JSON.</param>
    public string CreateAttestation(string creationOptionsJson)
    {
        var challenge = JsonDocument.Parse(creationOptionsJson).RootElement.GetProperty("challenge").GetString()!;
        var clientDataJson = ClientData("webauthn.create", challenge);
        var authData = BuildAuthData(flags: 0x45, signCount: 0, attestedCredentialData: BuildAttestedCredentialData());
        var attestationObject = EncodeAttestationObject(authData);

        return Serialize(new
        {
            id = CredentialIdB64,
            rawId = CredentialIdB64,
            type = "public-key",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = B64(clientDataJson),
                attestationObject = B64(attestationObject),
                transports = new[] { "internal" },
            },
        });
    }

    /// <summary>Produces the assertion credential JSON for a <c>navigator.credentials.get</c> equivalent.</summary>
    /// <param name="requestOptionsJson">The server's request-options JSON.</param>
    /// <param name="userId">The account id, sent as the user handle (discoverable credentials).</param>
    /// <param name="advanceSignCount">Whether to increment the signature counter (set false to simulate a replay).</param>
    public string CreateAssertion(string requestOptionsJson, string userId, bool advanceSignCount = true)
    {
        var challenge = JsonDocument.Parse(requestOptionsJson).RootElement.GetProperty("challenge").GetString()!;
        var clientDataJson = ClientData("webauthn.get", challenge);

        if (advanceSignCount)
        {
            _signCount++;
        }

        var authData = BuildAuthData(flags: 0x05, signCount: _signCount, attestedCredentialData: null);
        var signature = _key.SignData(
            [.. authData, .. SHA256.HashData(clientDataJson)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return Serialize(new
        {
            id = CredentialIdB64,
            rawId = CredentialIdB64,
            type = "public-key",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = B64(clientDataJson),
                authenticatorData = B64(authData),
                signature = B64(signature),
                userHandle = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(userId)),
            },
        });
    }

    private byte[] ClientData(string type, string challengeB64Url) => Encoding.UTF8.GetBytes(Serialize(new
    {
        type,
        challenge = challengeB64Url,
        origin = Origin,
        crossOrigin = false,
    }));

    private byte[] BuildAuthData(byte flags, uint signCount, byte[]? attestedCredentialData)
    {
        var buffer = new byte[37 + (attestedCredentialData?.Length ?? 0)];
        SHA256.HashData(Encoding.UTF8.GetBytes(RpId)).CopyTo(buffer.AsSpan(0, 32));
        buffer[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(33, 4), signCount);
        attestedCredentialData?.CopyTo(buffer.AsSpan(37));
        return buffer;
    }

    private byte[] BuildAttestedCredentialData()
    {
        var cose = EncodeCoseKey();
        var buffer = new byte[16 + 2 + CredentialId.Length + cose.Length];
        // aaguid stays all-zero.
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(16, 2), (ushort)CredentialId.Length);
        CredentialId.CopyTo(buffer.AsSpan(18));
        cose.CopyTo(buffer.AsSpan(18 + CredentialId.Length));
        return buffer;
    }

    private byte[] EncodeCoseKey()
    {
        var p = _key.ExportParameters(includePrivateParameters: false);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(5);
        writer.WriteInt32(1);   // kty
        writer.WriteInt32(2);   //   EC2
        writer.WriteInt32(3);   // alg
        writer.WriteInt32(-7);  //   ES256
        writer.WriteInt32(-1);  // crv
        writer.WriteInt32(1);   //   P-256
        writer.WriteInt32(-2);  // x
        writer.WriteByteString(p.Q.X!);
        writer.WriteInt32(-3);  // y
        writer.WriteByteString(p.Q.Y!);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] EncodeAttestationObject(byte[] authData)
    {
        var writer = new CborWriter();
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString("none");
        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        writer.WriteTextString("authData");
        writer.WriteByteString(authData);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static string B64(byte[] bytes) => WebEncoders.Base64UrlEncode(bytes);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
}
