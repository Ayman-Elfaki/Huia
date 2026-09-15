using System.Diagnostics;
using System.Net.Http;

namespace Huia.E2ETests;

/// <summary>
/// Boots the <c>Shop.Api</c>/<c>Shop.Nuxt</c> sample out-of-process on fixed HTTP ports so Playwright can
/// drive the <c>nuxt-huia-headless</c> module end to end: register, login, browse, cart, checkout,
/// logout, all against a real <c>Huia.Headless</c> bearer-token backend. Also boots its own
/// <c>Huia.External</c> instance (a separate port from <see cref="FrontEndStackFixture"/>'s, since xUnit
/// collections can run concurrently) so external login has a real upstream IdP to complete a genuine
/// challenge/callback round trip against, not just a stand-in.
///
/// Any missing build output or start-up failure leaves <see cref="Started"/> false and the specs skip.
/// </summary>
public sealed class ShopStackFixture : IAsyncLifetime
{
    private readonly List<Process> _processes = [];
    private readonly string _repoRoot = RepoRoot.Find();

    public string ShopApiUrl { get; } = "http://localhost:5341";
    public string ShopAppUrl { get; } = "http://localhost:3040";
    public string ExternalIssuer { get; } = "http://localhost:5322";

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
        var shopApiDll = Path.Combine(_repoRoot, "samples", "Shop", "Shop.Api", "bin", "Release", "net10.0", "Shop.Api.dll");
        var shopAppOutput = Path.Combine(_repoRoot, "samples", "Shop", "Shop.Nuxt", ".output", "server", "index.mjs");
        var externalDll = Path.Combine(_repoRoot, "samples", "Shared", "Huia.External", "bin", "Release", "net10.0", "Huia.External.dll");

        foreach (var (label, path) in new[]
        {
            ("Shop.Api", shopApiDll), ("Shop.Nuxt/.output", shopAppOutput), ("Huia.External", externalDll),
        })
        {
            if (!File.Exists(path))
            {
                SkipReason = $"Missing build output for {label} ({path}). Build the Release .NET output and `npm run build` Shop.Nuxt.";
                return;
            }
        }

        StartDotnet(externalDll, new()
        {
            ["ASPNETCORE_URLS"] = ExternalIssuer,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Issuer"] = ExternalIssuer,
            // Only the "shop-api" client (registered for ShopConsumer:BaseUrl) matters here — the
            // "huia-idp" client's Consumer:BaseUrl is left at its own default since nothing in this
            // fixture uses it.
            ["ShopConsumer__BaseUrl"] = ShopApiUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2EShopExternal;Mode=Memory;Cache=Shared",
        });

        StartDotnet(shopApiDll, new()
        {
            ["ASPNETCORE_URLS"] = ShopApiUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Huia__Issuer"] = ShopApiUrl,
            ["Huia__ExternalIssuer"] = ExternalIssuer,
            ["Huia__EnableE2E"] = "true",
            ["Shop__AppUrl"] = ShopAppUrl,
            ["ConnectionStrings__huia"] = "DataSource=E2EShop;Mode=Memory;Cache=Shared",
        });

        StartNode(shopAppOutput, new()
        {
            ["PORT"] = new Uri(ShopAppUrl).Port.ToString(),
            // Nitro's runtime override for nested runtimeConfig keys ("NUXT_" + SCREAMING_SNAKE path) —
            // the built .output already baked in the build-time defaults, so overriding the backend URL
            // post-build (fixed test ports) only works through this convention, not NUXT_PUBLIC_*.
            ["NUXT_SHOP_API_URL"] = ShopApiUrl,
            ["NUXT_HUIA_HEADLESS_BASE_URL"] = ShopApiUrl,
            ["NUXT_HUIA_HEADLESS_SESSION_PASSWORD"] = "e2e-only-shop-session-password-0123456789abcdef",
        });

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var ready =
            await WaitForAsync(probe, $"{ExternalIssuer}/partners/.well-known/openid-configuration")
            && await WaitForAsync(probe, $"{ShopApiUrl}/products")
            && await WaitForAsync(probe, $"{ShopAppUrl}/");

        Started = ready && _processes.All(p => !p.HasExited);
        if (!Started && SkipReason is null)
        {
            SkipReason = "Shop.Api, Huia.External, or Shop.Nuxt did not become ready in time.";
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
        // Node's undici rejects the ASP.NET Core dev cert; also lets nuxt-huia-headless call Shop.Api.
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

[CollectionDefinition("shop")]
public sealed class ShopStackCollection : ICollectionFixture<ShopStackFixture>;
