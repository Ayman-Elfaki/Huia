using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Huia.AppHost;

/// <summary>
/// Adds Aspire custom resource commands (<see cref="ResourceBuilderExtensions.WithCommand{T}"/>)
/// to build production E2E artifacts directly from the Aspire Dashboard or via <see cref="ResourceCommandService"/>.
/// </summary>
public static class E2ECommandExtensions
{
    public static IResourceBuilder<T> WithBuildE2EArtifactCommand<T>(
        this IResourceBuilder<T> builder,
        string relativeTarget,
        bool isNpm = false) where T : IResource
    {
        return builder.WithCommand(
            name: "build-e2e-artifact",
            displayName: "Build E2E Artifact",
            executeCommand: async context =>
            {
                var logger = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger("E2EArtifacts");
                logger.LogInformation("Building E2E artifact for {ResourceName} ({Target})...", builder.Resource.Name, relativeTarget);
                try
                {
                    var repoRoot = FindRepoRoot();
                    await RunBuildAsync(repoRoot, relativeTarget, isNpm, context.CancellationToken);
                    return CommandResults.Success($"Successfully built E2E artifact for {builder.Resource.Name}.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed building E2E artifact for {ResourceName}", builder.Resource.Name);
                    return CommandResults.Failure(ex.Message);
                }
            },
            commandOptions: new CommandOptions
            {
                IconName = "Wrench",
                Description = $"Builds production artifact for {builder.Resource.Name} used in E2E tests"
            });
    }

    public static IResourceBuilder<T> WithBuildAllE2EArtifactsCommand<T>(
        this IResourceBuilder<T> builder) where T : IResource
    {
        return builder.WithCommand(
            name: "build-all-e2e-artifacts",
            displayName: "Build All E2E Artifacts",
            executeCommand: async context =>
            {
                var logger = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger("E2EArtifacts");
                logger.LogInformation("Building all E2E artifacts across the repository...");
                try
                {
                    var repoRoot = FindRepoRoot();
                    await BuildAllAsync(repoRoot, logger, context.CancellationToken);
                    return CommandResults.Success("All E2E artifacts built successfully.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed building all E2E artifacts");
                    return CommandResults.Failure(ex.Message);
                }
            },
            commandOptions: new CommandOptions
            {
                IconName = "Building",
                Description = "Builds all Release .NET binaries and frontend packages (Nuxt, Next.js) required by E2E tests"
            });
    }

    public static async Task BuildAllAsync(string repoRoot, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var dotNetProjects = new[]
        {
            "samples/Shared/Huia.External/Huia.External.csproj",
            "samples/Todo/Todo.IdentityServer/Todo.IdentityServer.csproj",
            "samples/Todo/Todo.Api/Todo.Api.csproj",
            "samples/Shop/Shop.Api/Shop.Api.csproj",
            "samples/Shared/Huia.Cli/Huia.Cli.csproj"
        };

        foreach (var project in dotNetProjects)
        {
            logger?.LogInformation("Building {Project} in Release configuration...", project);
            await RunBuildAsync(repoRoot, project, isNpm: false, cancellationToken);
        }

        var npmDirs = new[]
        {
            "samples/Todo/Todo.Nuxt",
            "samples/Todo/Todo.Admin",
            "samples/Shop/Shop.Nuxt",
            "samples/Todo/Todo.Next",
            "samples/Shop/Shop.Next",
            "src/javascript/nuxt/nuxt-huia-oidc/playground"
        };

        foreach (var dir in npmDirs)
        {
            logger?.LogInformation("Building {Directory} via npm...", dir);
            await RunBuildAsync(repoRoot, dir, isNpm: true, cancellationToken);
        }
    }

    public static async Task RunBuildAsync(string repoRoot, string target, bool isNpm, CancellationToken cancellationToken = default)
    {
        string exe;
        string[] args;
        string? workingDir = null;

        if (isNpm)
        {
            workingDir = Path.Combine(repoRoot, target.Replace('/', Path.DirectorySeparatorChar));
            if (OperatingSystem.IsWindows())
            {
                exe = "cmd.exe";
                args = ["/c", "npm", "run", "build"];
            }
            else
            {
                exe = "npm";
                args = ["run", "build"];
            }
        }
        else
        {
            var projectFile = Path.Combine(repoRoot, target.Replace('/', Path.DirectorySeparatorChar));
            exe = "dotnet";
            args = ["build", projectFile, "-c", "Release", "--nologo"];
        }

        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDir ?? repoRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch process {exe}");

        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Build for '{target}' failed with exit code {process.ExitCode}:\n{output}");
        }
    }

    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null
            && !File.Exists(Path.Combine(dir, "src", "dotnet", "Huia.slnx"))
            && !Directory.Exists(Path.Combine(dir, ".git")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
