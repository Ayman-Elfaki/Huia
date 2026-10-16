using System.Diagnostics;
using System.Net.Http;

namespace Huia.E2ETests;

/// <summary>
/// Boots the TanStack Start sample stack out-of-process on fixed HTTP ports so Playwright can
/// test <c>Todo.TanStack</c> (via <c>@huia/tanstack-start-oidc</c>) against <c>Todo.IdentityServer</c> and <c>Todo.Api</c>,
/// and <c>Shop.TanStack</c> (via <c>@huia/tanstack-start-headless</c>) against <c>Shop.Api</c>.
///
/// Any missing build output or start-up failure leaves <see cref="Started"/> false and the specs skip.
/// </summary>
public sealed class TanStackFrontEndFixture : IAsyncLifetime
{
    private readonly List<Process> _processes = [];
    private readonly string _repoRoot = RepoRoot.Find();

    public string Issuer { get; } = "http://localhost:5326";
    public string ExternalIssuer { get; } = "http://localhost:5329";
    public string TodoApiUrl { get; } = "http://localhost:5336";
    public string TodoTanStackUrl { get; } = "http://localhost:3070";

    public string ShopApiUrl { get; } = "http://localhost:5346";
    public string ShopTanStackUrl { get; } = "http://localhost:3080";

    public bool Started { get; private set; }
    public string? SkipReason { get; private set; }
    public IReadOnlyDictionary<string, string> Logs => _logs.ToDictionary(k => k.Key, v => v.Value.ToString());

    public async Task InitializeAsync()
    {
        try
        {
            await StartAsync().WaitAsync(TimeSpan.FromSeconds(150));
        }
        catch (Exception ex)
        {
            var combinedLogs = string.Join("\n--- \n", _logs.Select(kvp => $"[{kvp.Key}]:\n{kvp.Value}"));
            SkipReason = $"{ex.Message}\nLogs:\n{combinedLogs}";
            Console.WriteLine($"[TanStackFrontEndFixture] Error: {SkipReason}");
            Started = false;
        }
    }

    private static string ResolveDll(string repoRoot, string sampleSubpath, string projectName)
    {
        var baseDir = Path.Combine(repoRoot, "samples", sampleSubpath, "bin", "Release");
        var candidate11 = Path.Combine(baseDir, "net11.0", $"{projectName}.dll");
        if (File.Exists(candidate11)) return candidate11;
        var candidate10 = Path.Combine(baseDir, "net10.0", $"{projectName}.dll");
        if (File.Exists(candidate10)) return candidate10;
        var debug11 = Path.Combine(repoRoot, "samples", sampleSubpath, "bin", "Debug", "net11.0", $"{projectName}.dll");
        if (File.Exists(debug11)) return debug11;
        return candidate11;
    }

    private async Task StartAsync()
    {
        await E2EArtifacts.EnsureTanStackStackAsync(_repoRoot);

        var idpDll = ResolveDll(_repoRoot, Path.Combine("Todo", "Todo.IdentityServer"), "Todo.IdentityServer");
        var externalDll = ResolveDll(_repoRoot, Path.Combine("Shared", "Huia.External"), "Huia.External");
        var todoApiDll = ResolveDll(_repoRoot, Path.Combine("Todo", "Todo.Api"), "Todo.Api");
        var shopApiDll = ResolveDll(_repoRoot, Path.Combine("Shop", "Shop.Api"), "Shop.Api");
        var todoTanStackDir = Path.Combine(_repoRoot, "samples", "Todo", "Todo.TanStack");
        var shopTanStackDir = Path.Combine(_repoRoot, "samples", "Shop", "Shop.TanStack");

        StartDotnet(externalDll, new()
        {
            ["ASPNETCORE_URLS"] = ExternalIssuer,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Issuer"] = ExternalIssuer,
            ["Consumer__BaseUrl"] = Issuer,
            ["ShopConsumer__BaseUrl"] = ShopApiUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2ETanStackExternal;Mode=Memory;Cache=Shared",
        }, "external");

        StartDotnet(idpDll, new()
        {
            ["ASPNETCORE_URLS"] = Issuer,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Database"] = "Sqlite",
            ["Huia__Issuer"] = Issuer,
            ["Huia__ExternalIssuer"] = ExternalIssuer,
            ["Huia__EnableE2E"] = "true",
            ["Huia__EnableBackgroundJobs"] = "false",
            ["Clients__TodoApp__BaseUrl"] = TodoTanStackUrl,
            ["Clients__TodoNext__BaseUrl"] = TodoTanStackUrl,
            ["Clients__TodoTanStack__BaseUrl"] = TodoTanStackUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2ETanStackIdp;Mode=Memory;Cache=Shared",
        }, "idp");

        StartDotnet(todoApiDll, new()
        {
            ["ASPNETCORE_URLS"] = TodoApiUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__BaseUrl"] = Issuer,
            ["Todo__PublicUrl"] = TodoApiUrl,
            ["Todo__Database"] = "Sqlite",
        }, "todo-api");

        StartDotnet(shopApiDll, new()
        {
            ["ASPNETCORE_URLS"] = ShopApiUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Issuer"] = ShopApiUrl,
            ["Huia__ExternalIssuer"] = ExternalIssuer,
            ["Huia__EnableE2E"] = "true",
            ["Huia__Database"] = "Sqlite",
            ["Shop__AppUrl"] = ShopTanStackUrl,
            ["Shop__NextAppUrl"] = ShopTanStackUrl,
            ["Shop__TanStackAppUrl"] = ShopTanStackUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2ETanStackShop;Mode=Memory;Cache=Shared",
        }, "shop-api");

        StartTanStack(todoTanStackDir, 3070, new()
        {
            ["PORT"] = "3070",
            ["APP_URL"] = TodoTanStackUrl,
            ["HUIA_BASE_URL"] = Issuer,
            ["TODO_API_URL"] = TodoApiUrl,
            ["HUIA_CLIENT_ID"] = "todo-tanstack",
            ["HUIA_CLIENT_SECRET"] = "todo-tanstack-secret",
            ["NODE_TLS_REJECT_UNAUTHORIZED"] = "0",
        }, "todo-tanstack");

        StartTanStack(shopTanStackDir, 3080, new()
        {
            ["PORT"] = "3080",
            ["APP_URL"] = ShopTanStackUrl,
            ["SHOP_API_URL"] = ShopApiUrl,
            ["NODE_TLS_REJECT_UNAUTHORIZED"] = "0",
        }, "shop-tanstack");

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        Console.WriteLine($"[TanStackFrontEndFixture] Probing ExternalIssuer: {ExternalIssuer}...");
        if (!await WaitForHttpAsync(probe, $"{ExternalIssuer}/partners/.well-known/openid-configuration"))
        {
            SkipReason = $"ExternalIssuer ({ExternalIssuer}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("external")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[TanStackFrontEndFixture] ExternalIssuer ready.");

        Console.WriteLine($"[TanStackFrontEndFixture] Probing Issuer: {Issuer}...");
        if (!await WaitForHttpAsync(probe, $"{Issuer}/todo/.well-known/openid-configuration"))
        {
            SkipReason = $"Issuer ({Issuer}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("idp")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[TanStackFrontEndFixture] Issuer ready.");

        Console.WriteLine($"[TanStackFrontEndFixture] Probing TodoApiUrl: {TodoApiUrl}...");
        if (!await WaitForHttpAsync(probe, $"{TodoApiUrl}/todos", accept: [401]))
        {
            SkipReason = $"TodoApiUrl ({TodoApiUrl}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("todo-api")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[TanStackFrontEndFixture] TodoApi ready.");

        Console.WriteLine($"[TanStackFrontEndFixture] Probing ShopApiUrl: {ShopApiUrl}...");
        if (!await WaitForHttpAsync(probe, $"{ShopApiUrl}/products"))
        {
            SkipReason = $"ShopApiUrl ({ShopApiUrl}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("shop-api")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[TanStackFrontEndFixture] ShopApi ready.");

        Console.WriteLine($"[TanStackFrontEndFixture] Probing TodoTanStackUrl: {TodoTanStackUrl}...");
        if (!await WaitForHttpAsync(probe, $"{TodoTanStackUrl}/"))
        {
            SkipReason = $"TodoTanStackUrl ({TodoTanStackUrl}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("todo-tanstack")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[TanStackFrontEndFixture] TodoTanStack ready.");

        Console.WriteLine($"[TanStackFrontEndFixture] Probing ShopTanStackUrl: {ShopTanStackUrl}...");
        if (!await WaitForHttpAsync(probe, $"{ShopTanStackUrl}/"))
        {
            SkipReason = $"ShopTanStackUrl ({ShopTanStackUrl}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("shop-tanstack")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[TanStackFrontEndFixture] ShopTanStack ready. All services started!");

        Started = true;
    }

    private readonly Dictionary<string, System.Text.StringBuilder> _logs = new();

    private void StartDotnet(string dllPath, Dictionary<string, string> env, string name)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(dllPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (k, v) in env) psi.Environment[k] = v;
        var p = Process.Start(psi)!;
        Track(p, name);
    }

    private void StartTanStack(string appDir, int port, Dictionary<string, string> env, string name)
    {
        var psi = new ProcessStartInfo("node", "start.mjs")
        {
            WorkingDirectory = appDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (k, v) in env) psi.Environment[k] = v;
        var p = Process.Start(psi)!;
        Track(p, name);
    }

    private void Track(Process process, string name)
    {
        var logs = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) logs.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) logs.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add(process);
        _logs[name] = logs;
    }

    private static async Task<bool> WaitForHttpAsync(HttpClient probe, string url, int[]? accept = null)
    {
        for (var i = 0; i < 45; i++)
        {
            try
            {
                var res = await probe.GetAsync(url);
                if (res.IsSuccessStatusCode || (accept?.Contains((int)res.StatusCode) ?? false)) return true;
            }
            catch { }
            await Task.Delay(1000);
        }
        return false;
    }

    public Task DisposeAsync()
    {
        foreach (var p in _processes)
        {
            try
            {
                if (!p.HasExited)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Process.Start(new ProcessStartInfo("taskkill", $"/F /T /PID {p.Id}") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000);
                    }
                    else
                    {
                        p.Kill(entireProcessTree: true);
                    }
                }
            }
            catch { }
            p.Dispose();
        }
        return Task.CompletedTask;
    }
}

[CollectionDefinition("tanstack-frontend")]
public sealed class TanStackFrontEndCollection : ICollectionFixture<TanStackFrontEndFixture>;
