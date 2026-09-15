using System.Text.Json;
using System.Text.Json.Serialization;

namespace Huia.Cli;

/// <summary>A cached token set for one (issuer, tenant), persisted under <c>~/.huia/tokens.json</c>.</summary>
public sealed record CachedTokens
{
    [JsonPropertyName("issuer")] public required string Issuer { get; init; }
    [JsonPropertyName("tenant")] public required string Tenant { get; init; }
    [JsonPropertyName("access_token")] public required string AccessToken { get; init; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; init; }

    public bool IsExpired(TimeProvider clock) => clock.GetUtcNow() >= ExpiresAt - TimeSpan.FromSeconds(30);
}

/// <summary>Reads and writes the CLI token cache. The directory is <c>$HUIA_CLI_HOME</c> or <c>~/.huia</c>.</summary>
public sealed class TokenStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;

    public TokenStore()
    {
        var home = Environment.GetEnvironmentVariable("HUIA_CLI_HOME")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".huia");
        _path = Path.Combine(home, "tokens.json");
    }

    /// <summary>The resolved cache file path (for diagnostics and tests).</summary>
    public string FilePath => _path;

    public CachedTokens? Read()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CachedTokens>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(CachedTokens tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(tokens, Json));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>Removes the cache file. Returns <see langword="true"/> if a file was deleted.</summary>
    public bool Clear()
    {
        if (!File.Exists(_path))
        {
            return false;
        }

        File.Delete(_path);
        return true;
    }
}
