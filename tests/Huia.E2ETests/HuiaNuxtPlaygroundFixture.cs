using System.Diagnostics;
using System.Net.Http;

namespace Huia.E2ETests;

/// <summary>
/// Boots <c>Huia.IdentityServer</c> (SQLite, E2E surface on) plus the built <c>huia-nuxt</c>
/// playground (<c>node .output/server/index.mjs</c>) so Playwright can drive a real OIDC round-trip
/// through the first-party module. Any missing build output or start-up failure leaves
/// <see cref="Started"/> false and the specs skip.
/// </summary>
public sealed class HuiaNuxtPlaygroundFixture : IAsyncLifetime
{
    private readonly List<Process> _processes = [];
    private readonly string _repoRoot = RepoRoot.Find();

    public string Issuer { get; } = "http://localhost:5319";
    public string PlaygroundUrl { get; } = "http://localhost:3030";

    public bool Started { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await StartAsync().WaitAsync(TimeSpan.FromSeconds(150));
        }
        catch (Exception ex)
        {
            SkipReason = ex.Message;
            Started = false;
        }
    }

    private async Task StartAsync()
    {
        var idpDll = Path.Combine(_repoRoot, "samples", "Huia.IdentityServer", "bin", "Release", "net10.0", "Huia.IdentityServer.dll");
        var playgroundEntry = Path.Combine(_repoRoot, "src", "nuxt", "playground", ".output", "server", "index.mjs");

        if (!File.Exists(idpDll))
        {
            SkipReason = $"Missing {idpDll}. Build the Release .NET output.";
            return;
        }
        if (!File.Exists(playgroundEntry))
        {
            SkipReason = $"Missing {playgroundEntry}. Run `npm --prefix src/nuxt run dev:build`.";
            return;
        }

        StartDotnet(idpDll, new()
        {
            ["ASPNETCORE_URLS"] = Issuer,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Database"] = "Sqlite",
            ["Huia__Issuer"] = Issuer,
            ["Huia__EnableE2E"] = "true",
            ["Huia__EnableBackgroundJobs"] = "false",
            ["Clients__PlaygroundApp__BaseUrl"] = PlaygroundUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2ENuxtAuth;Mode=Memory;Cache=Shared",
        });

        StartNode(playgroundEntry, new()
        {
            ["PORT"] = new Uri(PlaygroundUrl).Port.ToString(),
            ["NITRO_PORT"] = new Uri(PlaygroundUrl).Port.ToString(),
            ["NUXT_HUIA_AUTH_HUIA_BASE_URL"] = Issuer,
            ["NUXT_HUIA_AUTH_HUIA_TENANT"] = "e2e",
            ["NUXT_HUIA_AUTH_CLIENT_ID"] = "huia-nuxt-playground",
            ["NUXT_HUIA_AUTH_CLIENT_SECRET"] = "huia-nuxt-playground-secret",
            ["NUXT_HUIA_AUTH_SESSION_PASSWORD"] = "e2e-only-huia-nuxt-session-password-0123456789",
        });

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var ready =
            await WaitForAsync(probe, $"{Issuer}/e2e/.well-known/openid-configuration")
            && await WaitForAsync(probe, $"{PlaygroundUrl}/");

        Started = ready && _processes.All(p => !p.HasExited);
        if (!Started && SkipReason is null)
        {
            SkipReason = "The identity server or the playground did not become ready in time.";
        }
    }

    private void StartDotnet(string dll, Dictionary<string, string> env)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        info.ArgumentList.Add(dll);
        foreach (var (k, v) in env)
        {
            info.Environment[k] = v;
        }

        Track(info);
    }

    private void StartNode(string entry, Dictionary<string, string> env)
    {
        var info = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(entry))!,
        };
        info.ArgumentList.Add(entry);
        // Node's undici rejects the ASP.NET Core dev cert; also lets huia-nuxt discover a plain-http issuer.
        info.Environment["NODE_TLS_REJECT_UNAUTHORIZED"] = "0";
        foreach (var (k, v) in env)
        {
            info.Environment[k] = v;
        }

        Track(info);
    }

    private void Track(ProcessStartInfo info)
    {
        var process = Process.Start(info) ?? throw new InvalidOperationException($"Failed to start {info.FileName}.");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add(process);
    }

    private static async Task<bool> WaitForAsync(HttpClient probe, string url)
    {
        for (var attempt = 0; attempt < 90; attempt++)
        {
            try
            {
                var response = await probe.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                // not up yet
            }

            await Task.Delay(1000);
        }

        return false;
    }

    public Task DisposeAsync()
    {
        foreach (var process in _processes)
        {
            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                if (OperatingSystem.IsWindows())
                {
                    using var kill = Process.Start(new ProcessStartInfo("taskkill", $"/F /T /PID {process.Id}")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    kill?.WaitForExit(5000);
                }
                else
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best effort
            }
            finally
            {
                process.Dispose();
            }
        }

        return Task.CompletedTask;
    }
}

[CollectionDefinition("huia-nuxt")]
public sealed class HuiaNuxtPlaygroundCollection : ICollectionFixture<HuiaNuxtPlaygroundFixture>;
