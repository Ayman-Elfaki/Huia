using System.Collections.Concurrent;
using System.Diagnostics;

namespace Huia.E2ETests;

internal static class E2EArtifacts
{
    private static readonly ConcurrentDictionary<string, Task> Builds = new();

    public static Task EnsureIdentityServerAsync(string repoRoot)
        => EnsureAsync("identity", repoRoot, "samples\\Todo\\Todo.IdentityServer\\Todo.IdentityServer.csproj", null,
            "samples\\Todo\\Todo.IdentityServer\\bin\\Release\\net10.0\\Todo.IdentityServer.dll");

    public static Task EnsureFrontendStackAsync(string repoRoot)
        => EnsureAsync("frontend", repoRoot, null, [
            ("samples\\Shared\\Huia.External\\Huia.External.csproj", null, "samples\\Shared\\Huia.External\\bin\\Release\\net10.0\\Huia.External.dll"),
            ("samples\\Todo\\Todo.IdentityServer\\Todo.IdentityServer.csproj", null, "samples\\Todo\\Todo.IdentityServer\\bin\\Release\\net10.0\\Todo.IdentityServer.dll"),
            ("samples\\Todo\\Todo.Api\\Todo.Api.csproj", null, "samples\\Todo\\Todo.Api\\bin\\Release\\net10.0\\Todo.Api.dll"),
            (null, "samples\\Todo\\Todo.Nuxt", "samples\\Todo\\Todo.Nuxt\\.output\\server\\index.mjs"),
            (null, "samples\\Todo\\Todo.Admin", "samples\\Todo\\Todo.Admin\\.output\\server\\index.mjs"),
        ]);

    public static Task EnsureNextStackAsync(string repoRoot)
        => EnsureAsync("next", repoRoot, null, [
            ("samples\\Shared\\Huia.External\\Huia.External.csproj", null, "samples\\Shared\\Huia.External\\bin\\Release\\net10.0\\Huia.External.dll"),
            ("samples\\Todo\\Todo.IdentityServer\\Todo.IdentityServer.csproj", null, "samples\\Todo\\Todo.IdentityServer\\bin\\Release\\net10.0\\Todo.IdentityServer.dll"),
            ("samples\\Todo\\Todo.Api\\Todo.Api.csproj", null, "samples\\Todo\\Todo.Api\\bin\\Release\\net10.0\\Todo.Api.dll"),
            ("samples\\Shop\\Shop.Api\\Shop.Api.csproj", null, "samples\\Shop\\Shop.Api\\bin\\Release\\net10.0\\Shop.Api.dll"),
            (null, "samples\\Todo\\Todo.Next", "samples\\Todo\\Todo.Next\\.next\\BUILD_ID"),
            (null, "samples\\Shop\\Shop.Next", "samples\\Shop\\Shop.Next\\.next\\BUILD_ID"),
        ]);

    public static Task EnsureShopStackAsync(string repoRoot)
        => EnsureAsync("shop", repoRoot, null, [
            ("samples\\Shared\\Huia.External\\Huia.External.csproj", null, "samples\\Shared\\Huia.External\\bin\\Release\\net10.0\\Huia.External.dll"),
            ("samples\\Shop\\Shop.Api\\Shop.Api.csproj", null, "samples\\Shop\\Shop.Api\\bin\\Release\\net10.0\\Shop.Api.dll"),
            (null, "samples\\Shop\\Shop.Nuxt", "samples\\Shop\\Shop.Nuxt\\.output\\server\\index.mjs"),
        ]);

    public static Task EnsureNuxtPlaygroundAsync(string repoRoot)
        => EnsureAsync("playground", repoRoot, null, [
            ("samples\\Todo\\Todo.IdentityServer\\Todo.IdentityServer.csproj", null, "samples\\Todo\\Todo.IdentityServer\\bin\\Release\\net10.0\\Todo.IdentityServer.dll"),
            (null, "src\\javascript\\nuxt\\nuxt-huia-oidc\\playground", "src\\javascript\\nuxt\\nuxt-huia-oidc\\playground\\.output\\server\\index.mjs"),
        ], "npm", ["run", "build"]);

    private static Task EnsureAsync(
        string key,
        string repoRoot,
        string? project,
        (string? Project, string? WorkingDirectory, string Output)[]? artifacts,
        string? executable = null,
        string[]? arguments = null)
        => Builds.GetOrAdd(key, _ => BuildMissingAsync(repoRoot, project, artifacts ?? [], executable, arguments));

    private static async Task BuildMissingAsync(
        string repoRoot,
        string? project,
        (string? Project, string? WorkingDirectory, string Output)[] artifacts,
        string? executable,
        string[]? arguments)
    {
        var targets = artifacts.Length == 0
            ? [(project, (string?)null, Path.Combine(repoRoot, "samples", "Todo", "Todo.IdentityServer", "bin", "Release", "net10.0", "Todo.IdentityServer.dll"))]
            : artifacts.Select(a => (a.Project, a.WorkingDirectory, Path.Combine(repoRoot, a.Output))).ToArray();

        foreach (var target in targets)
        {
            if (File.Exists(target.Item3) || Directory.Exists(target.Item3))
            {
                continue;
            }

            if (target.Item1 is not null)
            {
                await RunAsync(repoRoot, "dotnet", ["build", target.Item1, "-c", "Release", "--nologo"], null);
            }
            else
            {
                await RunAsync(repoRoot, executable ?? "npm", arguments ?? ["run", "build"], target.Item2);
            }

            if (!File.Exists(target.Item3) && !Directory.Exists(target.Item3))
            {
                throw new InvalidOperationException($"Build completed without producing expected artifact: {target.Item3}");
            }
        }
    }

    private static async Task RunAsync(string repoRoot, string executable, string[] arguments, string? workingDirectory)
    {
        var exe = executable;
        var args = arguments;
        if (OperatingSystem.IsWindows() && string.Equals(executable, "npm", StringComparison.OrdinalIgnoreCase))
        {
            exe = "cmd.exe";
            args = ["/c", "npm", ..arguments];
        }

        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDirectory is null ? repoRoot : Path.Combine(repoRoot, workingDirectory),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Failed to start {executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var details = $"{await output}\n{await error}".Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{executable} {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.\n{details}");
        }
    }
}