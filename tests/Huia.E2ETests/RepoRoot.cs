namespace Huia.E2ETests;

/// <summary>
/// Locates the monorepo root from a test binary. The .NET solution lives at
/// <c>src/dotnet/Huia.slnx</c>; <c>samples/</c>, <c>tests/</c> and <c>docs/</c> sit at the root.
/// </summary>
internal static class RepoRoot
{
    public static string Find()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null
            && !File.Exists(Path.Combine(dir, "src", "dotnet", "Huia.slnx"))
            && !Directory.Exists(Path.Combine(dir, ".git")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
