using System.Diagnostics;
using System.Net.Http;

namespace Huia.E2ETests;

/// <summary>
/// Boots the whole sample stack out-of-process on fixed HTTP ports so Playwright can drive the two Nuxt
/// front-ends through a real OIDC round-trip: <c>Huia.IdentityServer</c> (E2E surface on),
/// <c>Huia.External</c> (upstream provider for the external-login flow), <c>Todo.Api</c> (resource
/// server), and <c>npm run preview</c> for <c>Todo.App</c> and <c>Huia.AdminUI</c>.
///
/// Any missing build output or start-up failure leaves <see cref="Started"/> false and the specs skip.
/// </summary>
public sealed class FrontEndStackFixture : IAsyncLifetime
{
    private readonly List<Process> _processes = [];
    private readonly string _repoRoot = FindRepoRoot();

    public string Issuer { get; } = "http://localhost:5320";
    public string ExternalIssuer { get; } = "http://localhost:5321";
    public string TodoApiUrl { get; } = "http://localhost:5332";
    public string TodoAppUrl { get; } = "http://localhost:3020";
    public string AdminAppUrl { get; } = "http://localhost:3021";

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
        var idpDll = DllPath("samples", "Huia.IdentityServer");
        var externalDll = DllPath("samples", "Huia.External");
        var todoApiDll = DllPath("samples", "Todo.Api");
        var todoAppOutput = Path.Combine(_repoRoot, "samples", "Todo.App", ".output", "server", "index.mjs");
        var adminAppOutput = Path.Combine(_repoRoot, "samples", "Huia.AdminUI", ".output", "server", "index.mjs");

        foreach (var (label, path) in new[]
        {
            ("Huia.IdentityServer", idpDll), ("Huia.External", externalDll), ("Todo.Api", todoApiDll),
            ("Todo.App/.output", todoAppOutput), ("Huia.AdminUI/.output", adminAppOutput),
        })
        {
            if (!File.Exists(path))
            {
                SkipReason = $"Missing build output for {label} ({path}). Build the Release .NET output and `npm run build` both Nuxt apps.";
                return;
            }
        }

        StartDotnet(externalDll, new()
        {
            ["ASPNETCORE_URLS"] = ExternalIssuer,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Issuer"] = ExternalIssuer,
            ["Consumer__BaseUrl"] = Issuer,
            ["ConnectionStrings__huia"] = "DataSource=E2EExternal;Mode=Memory;Cache=Shared",
        });

        StartDotnet(idpDll, new()
        {
            ["ASPNETCORE_URLS"] = Issuer,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Database"] = "Sqlite",
            ["Huia__Issuer"] = Issuer,
            ["Huia__ExternalIssuer"] = ExternalIssuer,
            ["Huia__EnableE2E"] = "true",
            ["Huia__EnableBackgroundJobs"] = "false",
            ["Clients__TodoApp__BaseUrl"] = TodoAppUrl,
            ["Clients__AdminApp__BaseUrl"] = AdminAppUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2EFrontend;Mode=Memory;Cache=Shared",
        });

        StartDotnet(todoApiDll, new()
        {
            ["ASPNETCORE_URLS"] = TodoApiUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__BaseUrl"] = Issuer,
        });

        // The built `.output` bakes the config-time URLs, so point every base URL at the E2E issuer
        // through the runtime-config env overrides (nuxt-oidc-auth + nuxt-api-party both read these).
        var commonNuxt = new Dictionary<string, string>
        {
            ["NUXT_OIDC_SESSION_SECRET"] = "e2e-only-session-secret-change-me-0123456789abcdef",
            // NUXT_OIDC_TOKEN_KEY must be a base64-encoded 32-byte AES key.
            ["NUXT_OIDC_TOKEN_KEY"] = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=",
            ["NUXT_OIDC_AUTH_SESSION_SECRET"] = "e2e-only-auth-session-secret-0123456789abcdefghij",
        };

        StartNode(todoAppOutput, new(commonNuxt)
        {
            ["PORT"] = new Uri(TodoAppUrl).Port.ToString(),
            ["NUXT_OIDC_PROVIDERS_OIDC_CLIENT_SECRET"] = "todo-app-secret",
            ["NUXT_OIDC_PROVIDERS_OIDC_REDIRECT_URI"] = $"{TodoAppUrl}/auth/oidc/callback",
            ["NUXT_OIDC_PROVIDERS_OIDC_AUTHORIZATION_URL"] = $"{Issuer}/todo/connect/authorize",
            ["NUXT_OIDC_PROVIDERS_OIDC_TOKEN_URL"] = $"{Issuer}/todo/connect/token",
            ["NUXT_OIDC_PROVIDERS_OIDC_USER_INFO_URL"] = $"{Issuer}/todo/connect/userinfo",
            ["NUXT_OIDC_PROVIDERS_OIDC_LOGOUT_URL"] = $"{Issuer}/todo/connect/logout",
            ["NUXT_API_PARTY_ENDPOINTS_HUIA_URL"] = $"{Issuer}/todo",
            ["NUXT_API_PARTY_ENDPOINTS_TODO_API_URL"] = TodoApiUrl,
        });

        StartNode(adminAppOutput, new(commonNuxt)
        {
            ["PORT"] = new Uri(AdminAppUrl).Port.ToString(),
            ["NUXT_OIDC_PROVIDERS_OIDC_CLIENT_SECRET"] = "huia-admin-ui-secret",
            ["NUXT_OIDC_PROVIDERS_OIDC_REDIRECT_URI"] = $"{AdminAppUrl}/auth/oidc/callback",
            ["NUXT_OIDC_PROVIDERS_OIDC_AUTHORIZATION_URL"] = $"{Issuer}/master/connect/authorize",
            ["NUXT_OIDC_PROVIDERS_OIDC_TOKEN_URL"] = $"{Issuer}/master/connect/token",
            ["NUXT_OIDC_PROVIDERS_OIDC_USER_INFO_URL"] = $"{Issuer}/master/connect/userinfo",
            ["NUXT_OIDC_PROVIDERS_OIDC_LOGOUT_URL"] = $"{Issuer}/master/connect/logout",
            ["NUXT_API_PARTY_ENDPOINTS_HUIA_URL"] = $"{Issuer}/master",
        });

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var ready =
            await WaitForAsync(probe, $"{ExternalIssuer}/partners/.well-known/openid-configuration")
            && await WaitForAsync(probe, $"{Issuer}/todo/.well-known/openid-configuration")
            && await WaitForAsync(probe, $"{TodoApiUrl}/todos", accept: [401])
            && await WaitForAsync(probe, $"{TodoAppUrl}/")
            && await WaitForAsync(probe, $"{AdminAppUrl}/");

        Started = ready && _processes.All(p => !p.HasExited);
        if (!Started && SkipReason is null)
        {
            SkipReason = "One or more sample processes did not become ready in time.";
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

    private static async Task<bool> WaitForAsync(HttpClient probe, string url, int[]? accept = null)
    {
        for (var attempt = 0; attempt < 90; attempt++)
        {
            try
            {
                var response = await probe.GetAsync(url);
                if (response.IsSuccessStatusCode || (accept?.Contains((int)response.StatusCode) ?? false))
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

    private string DllPath(params string[] projectSegments)
    {
        var name = projectSegments[^1];
        return Path.Combine(_repoRoot, Path.Combine(projectSegments), "bin", "Release", "net10.0", $"{name}.dll");
    }

    private static string FindRepoRoot()
    {
        // The repo root holds src/dotnet/Huia.slnx and the top-level samples/ + tests/ folders.
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

[CollectionDefinition("frontend-stack")]
public sealed class FrontEndStackCollection : ICollectionFixture<FrontEndStackFixture>;
