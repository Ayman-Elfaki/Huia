using System.Diagnostics;
using System.Net.Http;

namespace Huia.E2ETests;

/// <summary>
/// Runs the real <c>Huia.IdentityServer</c> as an out-of-process HTTP server (SQLite, E2E surface on) so
/// Playwright can drive it through a browser. Any failure here leaves <see cref="Started"/> false and the
/// browser tests skip rather than fail the run.
/// </summary>
public sealed class SampleHostFixture : IAsyncLifetime
{
    private Process? _process;

    /// <summary>The base URL the sample is listening on.</summary>
    public string BaseUrl { get; } = "http://localhost:5317";

    /// <summary>Whether the host actually started (false skips the browser tests).</summary>
    public bool Started { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await StartAsync().WaitAsync(TimeSpan.FromSeconds(90));
        }
        catch
        {
            Started = false;
        }
    }

    private async Task StartAsync()
    {
        var repoRoot = FindRepoRoot();
        var dllPath = Path.Combine(repoRoot, "samples", "Huia.IdentityServer", "bin", "Release", "net10.0", "Huia.IdentityServer.dll");
        if (!File.Exists(dllPath))
        {
            return;
        }

        var startInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dllPath)!,
        };
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["Huia__Database"] = "Sqlite";
        startInfo.Environment["Huia__Issuer"] = BaseUrl;
        startInfo.Environment["Huia__EnableE2E"] = "true";
        startInfo.Environment["Huia__EnableBackgroundJobs"] = "false";
        startInfo.Environment["ConnectionStrings__huia"] = "DataSource=E2E;Mode=Memory;Cache=Shared";

        _process = Process.Start(startInfo);
        if (_process is null)
        {
            return;
        }

        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < 60 && !_process.HasExited; attempt++)
        {
            try
            {
                var response = await probe.GetAsync($"{BaseUrl}/todo/.well-known/openid-configuration");
                if (response.IsSuccessStatusCode)
                {
                    Started = true;
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                // not up yet
            }

            await Task.Delay(1000);
        }
    }

    public Task DisposeAsync()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                if (OperatingSystem.IsWindows())
                {
                    using var kill = Process.Start(new ProcessStartInfo("taskkill", $"/F /T /PID {_process.Id}")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    kill?.WaitForExit(5000);
                }
                else
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            _process?.Dispose();
        }

        return Task.CompletedTask;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Huia.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}

[CollectionDefinition("sample-host")]
public sealed class SampleHostCollection : ICollectionFixture<SampleHostFixture>;
