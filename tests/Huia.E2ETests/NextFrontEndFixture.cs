using System.Diagnostics;
using System.Net.Http;

namespace Huia.E2ETests;

/// <summary>
/// Boots the Next.js sample stack out-of-process on fixed HTTP ports so Playwright can
/// test <c>Todo.Next</c> (via <c>next-huia-oidc</c>) against <c>Huia.IdentityServer</c> and <c>Todo.Api</c>,
/// and <c>Shop.Next</c> (via <c>next-huia-headless</c>) against <c>Shop.Api</c>.
///
/// Any missing build output or start-up failure leaves <see cref="Started"/> false and the specs skip.
/// </summary>
public sealed class NextFrontEndFixture : IAsyncLifetime
{
    private readonly List<Process> _processes = [];
    private readonly string _repoRoot = RepoRoot.Find();

    public string Issuer { get; } = "http://localhost:5325";
    public string ExternalIssuer { get; } = "http://localhost:5328";
    public string TodoApiUrl { get; } = "http://localhost:5335";
    public string TodoNextUrl { get; } = "http://localhost:3050";

    public string ShopApiUrl { get; } = "http://localhost:5345";
    public string ShopNextUrl { get; } = "http://localhost:3060";

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
            var combinedLogs = string.Join("\n--- \n", _logs.Select(kvp => $"[{kvp.Key}]:\n{kvp.Value}"));
            SkipReason = $"{ex.Message}\nLogs:\n{combinedLogs}";
            Console.WriteLine($"[NextFrontEndFixture] Error: {SkipReason}");
            Started = false;
        }
    }

    private async Task StartAsync()
    {
        var idpDll = Path.Combine(_repoRoot, "samples", "Huia.IdentityServer", "bin", "Release", "net10.0", "Huia.IdentityServer.dll");
        var externalDll = Path.Combine(_repoRoot, "samples", "Huia.External", "bin", "Release", "net10.0", "Huia.External.dll");
        var todoApiDll = Path.Combine(_repoRoot, "samples", "Todo.Api", "bin", "Release", "net10.0", "Todo.Api.dll");
        var shopApiDll = Path.Combine(_repoRoot, "samples", "Shop.Api", "bin", "Release", "net10.0", "Shop.Api.dll");
        var todoNextDir = Path.Combine(_repoRoot, "samples", "Todo.Next");
        var shopNextDir = Path.Combine(_repoRoot, "samples", "Shop.Next");

        if (!File.Exists(idpDll))
        {
            SkipReason = $"Missing {idpDll}. Build the Release .NET output.";
            return;
        }
        if (!File.Exists(externalDll))
        {
            SkipReason = $"Missing {externalDll}. Build the Release .NET output.";
            return;
        }
        if (!Directory.Exists(Path.Combine(todoNextDir, ".next")))
        {
            SkipReason = $"Missing .next build in {todoNextDir}. Run `npm run build` first.";
            return;
        }
        if (!Directory.Exists(Path.Combine(shopNextDir, ".next")))
        {
            SkipReason = $"Missing .next build in {shopNextDir}. Run `npm run build` first.";
            return;
        }

        StartDotnet(externalDll, new()
        {
            ["ASPNETCORE_URLS"] = ExternalIssuer,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Issuer"] = ExternalIssuer,
            ["Consumer__BaseUrl"] = Issuer,
            ["ShopConsumer__BaseUrl"] = ShopApiUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2ENextExternal;Mode=Memory;Cache=Shared",
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
            ["Clients__TodoApp__BaseUrl"] = TodoNextUrl,
            ["Clients__TodoNext__BaseUrl"] = TodoNextUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2ENextIdp;Mode=Memory;Cache=Shared",
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
            ["Shop__AppUrl"] = ShopNextUrl,
            ["Shop__NextAppUrl"] = ShopNextUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2ENextShop;Mode=Memory;Cache=Shared",
        }, "shop-api");

        StartNext(todoNextDir, 3050, new()
        {
            ["PORT"] = "3050",
            ["HUIA_BASE_URL"] = Issuer,
            ["TODO_API_URL"] = TodoApiUrl,
            ["HUIA_CLIENT_ID"] = "todo-next",
            ["HUIA_CLIENT_SECRET"] = "todo-next-secret",
            ["NODE_TLS_REJECT_UNAUTHORIZED"] = "0",
        }, "todo-next");

        StartNext(shopNextDir, 3060, new()
        {
            ["PORT"] = "3060",
            ["SHOP_API_URL"] = ShopApiUrl,
            ["NODE_TLS_REJECT_UNAUTHORIZED"] = "0",
        }, "shop-next");

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        Console.WriteLine($"[NextFrontEndFixture] Probing ExternalIssuer: {ExternalIssuer}...");
        if (!await WaitForHttpAsync(probe, $"{ExternalIssuer}/partners/.well-known/openid-configuration"))
        {
            SkipReason = $"ExternalIssuer ({ExternalIssuer}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("external")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[NextFrontEndFixture] ExternalIssuer ready.");

        Console.WriteLine($"[NextFrontEndFixture] Probing Issuer: {Issuer}...");
        if (!await WaitForHttpAsync(probe, $"{Issuer}/todo/.well-known/openid-configuration"))
        {
            SkipReason = $"Issuer ({Issuer}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("idp")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[NextFrontEndFixture] Issuer ready.");

        Console.WriteLine($"[NextFrontEndFixture] Probing TodoApiUrl: {TodoApiUrl}...");
        if (!await WaitForHttpAsync(probe, $"{TodoApiUrl}/todos", accept: [401]))
        {
            SkipReason = $"TodoApiUrl ({TodoApiUrl}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("todo-api")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[NextFrontEndFixture] TodoApi ready.");

        Console.WriteLine($"[NextFrontEndFixture] Probing ShopApiUrl: {ShopApiUrl}...");
        if (!await WaitForHttpAsync(probe, $"{ShopApiUrl}/products"))
        {
            SkipReason = $"ShopApiUrl ({ShopApiUrl}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("shop-api")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[NextFrontEndFixture] ShopApi ready.");

        Console.WriteLine($"[NextFrontEndFixture] Probing TodoNextUrl: {TodoNextUrl}...");
        if (!await WaitForHttpAsync(probe, $"{TodoNextUrl}/"))
        {
            SkipReason = $"TodoNextUrl ({TodoNextUrl}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("todo-next")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[NextFrontEndFixture] TodoNext ready.");

        Console.WriteLine($"[NextFrontEndFixture] Probing ShopNextUrl: {ShopNextUrl}...");
        if (!await WaitForHttpAsync(probe, $"{ShopNextUrl}/"))
        {
            SkipReason = $"ShopNextUrl ({ShopNextUrl}) failed to report ready. Logs:\n{_logs.GetValueOrDefault("shop-next")}";
            Console.WriteLine(SkipReason);
            return;
        }
        Console.WriteLine("[NextFrontEndFixture] ShopNext ready. All services started!");

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

    private void StartNext(string appDir, int port, Dictionary<string, string> env, string name)
    {
        var nextBin = Path.Combine(appDir, "node_modules", "next", "dist", "bin", "next");
        var psi = new ProcessStartInfo("node", $"\"{nextBin}\" start -p {port}")
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

[CollectionDefinition("next-frontend")]
public sealed class NextFrontEndCollection : ICollectionFixture<NextFrontEndFixture>;
