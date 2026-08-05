using System.Net;
using System.Text.Json;

namespace Huia.Tests.Integration;

/// <summary>
/// Penetration tests for <c>UseAutomaticKeyManagement</c> (<see cref="Huia.Keys.KeyManagementBuilder"/>),
/// covering the one surface it exposes to an unauthenticated network attacker: the public JWKS document
/// (<c>/.well-known/jwks</c>, linked from <c>jwks_uri</c> in discovery). <see cref="Huia.Keys.KeyManager"/>
/// holds both a signing keypair and an encryption keypair per <see cref="Huia.Keys.KeyUsage"/>, and
/// <see cref="Huia.Keys.ServerOptionsConfigurator"/> feeds both into OpenIddict — these tests prove
/// that only the signing key's *public* parameters ever reach that document. Getting either wrong is
/// catastrophic: leaking a private exponent lets anyone forge arbitrarily-scoped tokens, and publishing the
/// encryption key would let anyone decrypt every access token the server issues, defeating the confidentiality
/// <c>DisableAccessTokenEncryption()</c> being left off (the default) is supposed to buy. Key lifecycle
/// correctness (rotation timing, retirement, validation-overlap, purge) is already covered at the unit level in
/// <c>HuiaKeyManagementBuilderTests</c>; this file only exercises what's reachable over HTTP.
/// </summary>
public class KeySecurityTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private static readonly string[] RsaPrivateComponentNames = ["d", "p", "q", "dp", "dq", "qi", "oth", "k"];

    [Fact]
    public async Task Jwks_NeverExposesPrivateKeyMaterial()
    {
        var keys = await GetJwksKeysAsync();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            foreach (var privateComponent in RsaPrivateComponentNames)
            {
                Assert.False(key.TryGetProperty(privateComponent, out _),
                    $"JWKS key '{key.GetProperty("kid").GetString()}' exposed private RSA component '{privateComponent}'.");
            }
        }
    }

    /// <summary>
    /// <see cref="Huia.Keys.ServerOptionsConfigurator"/> feeds <c>Snapshot.EncryptionKeys</c> into
    /// <c>OpenIddictServerOptions.EncryptionCredentials</c> alongside the signing keys in
    /// <c>SigningCredentials</c> — unlike signing keys, an encryption key is only ever needed by the party that
    /// decrypts (the server itself, for the JWEs it issued to itself), never by a relying party, so OpenIddict
    /// does not publish it. This locks that in: if a future change (e.g. switching resource-server validation
    /// to a shared JWKS instead of introspection) ever starts publishing it, every already-issued access token's
    /// confidentiality would be retroactively broken, and this test starts failing.
    /// </summary>
    [Fact]
    public async Task Jwks_NeverPublishesTheEncryptionKey()
    {
        var keys = await GetJwksKeysAsync();

        Assert.All(keys, key => Assert.Equal("sig", key.GetProperty("use").GetString()));
    }

    /// <summary>
    /// Guards against a misconfigured/downgraded <c>RsaKeySizeInBits</c> (default 2048) slipping through:
    /// a small enough RSA modulus is factorable, which — for a signing key — means an attacker who never
    /// needed the private key in the first place can derive it and forge tokens outright.
    /// </summary>
    [Fact]
    public async Task Jwks_SigningKey_MeetsMinimumRsaStrength()
    {
        var keys = await GetJwksKeysAsync();

        foreach (var key in keys)
        {
            Assert.Equal("RSA", key.GetProperty("kty").GetString());
            var modulusBits = Base64UrlDecode(key.GetProperty("n").GetString()!).Length * 8;
            Assert.True(modulusBits >= 2048,
                $"Signing key '{key.GetProperty("kid").GetString()}' has only a {modulusBits}-bit modulus.");
        }
    }

    [Fact]
    public async Task Jwks_KeyIds_AreUnique()
    {
        var keys = await GetJwksKeysAsync();

        var keyIds = keys.Select(k => k.GetProperty("kid").GetString()).ToList();
        Assert.Equal(keyIds.Distinct().Count(), keyIds.Count);
    }

    private async Task<List<JsonElement>> GetJwksKeysAsync()
    {
        var client = factory.CreateClient();

        using var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
        using var discoveryDocument = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync());
        var jwksUri = discoveryDocument.RootElement.GetProperty("jwks_uri").GetString()!;

        using var jwksResponse = await client.GetAsync(jwksUri);
        Assert.Equal(HttpStatusCode.OK, jwksResponse.StatusCode);
        using var jwks = JsonDocument.Parse(await jwksResponse.Content.ReadAsStringAsync());

        return [.. jwks.RootElement.GetProperty("keys").EnumerateArray().Select(k => k.Clone())];
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}