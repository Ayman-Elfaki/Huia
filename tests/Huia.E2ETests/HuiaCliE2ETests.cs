using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace Huia.E2ETests;

/// <summary>
/// Drives the compiled <c>Huia.Cli</c> against the running sample identity server: a real
/// device-authorization sign-in (the user code is approved over HTTP the way a browser would),
/// then <c>whoami</c> and <c>logout</c>.
/// </summary>
[Trait("Category", "E2E")]
[Collection("sample-host")]
public sealed partial class HuiaCliE2ETests(SampleHostFixture host)
{
    private static readonly string CliDll = Path.Combine(
        RepoRoot.Find(), "samples", "Huia.Cli", "bin", "Release", "net10.0", "huia.dll");

    [SkippableFact]
    public async Task Device_login_then_whoami_then_logout()
    {
        Skip.IfNot(host.Started, "The sample identity server is not running.");
        Skip.IfNot(File.Exists(CliDll), "Huia.Cli has not been built in Release.");

        var cliHome = Path.Combine(Path.GetTempPath(), "huia-cli-e2e-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var login = StartCli(cliHome, "login", "--issuer", host.BaseUrl, "--tenant", "master");

            var userCode = await ReadUserCodeAsync(login);
            await ApproveDeviceCodeAsync(userCode);

            var loginExit = await WaitAsync(login, TimeSpan.FromSeconds(30));
            loginExit.ShouldBe(0, login.Output);
            login.Output.ShouldContain("Signed in");

            using var whoami = StartCli(cliHome, "whoami", "--issuer", host.BaseUrl, "--tenant", "master");
            (await WaitAsync(whoami, TimeSpan.FromSeconds(20))).ShouldBe(0, whoami.Output);
            whoami.Output.ShouldContain("admin@huia.local");

            Path.Combine(cliHome, "tokens.json").ShouldSatisfyAllConditions(
                () => File.Exists(Path.Combine(cliHome, "tokens.json")).ShouldBeTrue());

            using var logout = StartCli(cliHome, "logout");
            (await WaitAsync(logout, TimeSpan.FromSeconds(10))).ShouldBe(0, logout.Output);
            File.Exists(Path.Combine(cliHome, "tokens.json")).ShouldBeFalse();
        }
        finally
        {
            try { Directory.Delete(cliHome, recursive: true); } catch { /* best effort */ }
        }
    }

    private async Task ApproveDeviceCodeAsync(string userCode)
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(host.BaseUrl) };

        var loginPage = await client.GetStringAsync("/master/identity/account/login");
        var signIn = await client.PostAsync("/master/identity/account/login", Form(new()
        {
            ["Input.Email"] = "admin@huia.local",
            ["Input.Password"] = "Admin1!Pass",
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = Antiforgery(loginPage),
        }));
        signIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var toPage = await client.GetAsync($"/master/connect/verify?user_code={Uri.EscapeDataString(userCode)}");
        toPage.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var pageHtml = await client.GetStringAsync(toPage.Headers.Location!.IsAbsoluteUri
            ? toPage.Headers.Location.PathAndQuery
            : toPage.Headers.Location!.ToString());

        var approve = await client.PostAsync("/master/connect/verify", Form(new()
        {
            ["user_code"] = userCode,
            ["submit.accept"] = "accept",
            ["__RequestVerificationToken"] = Antiforgery(pageHtml),
        }));
        approve.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect, HttpStatusCode.Found);
    }

    private static async Task<string> ReadUserCodeAsync(CliProcess cli)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!cts.IsCancellationRequested)
        {
            var match = UserCodeRegex().Match(cli.Output);
            if (match.Success)
            {
                return match.Groups["code"].Value;
            }

            await Task.Delay(200, cts.Token);
        }

        throw new InvalidOperationException("The CLI never printed a user code. Output:\n" + cli.Output);
    }

    private static CliProcess StartCli(string cliHome, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(CliDll);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment["HUIA_CLI_HOME"] = cliHome;
        return new CliProcess(startInfo);
    }

    private static async Task<int> WaitAsync(CliProcess cli, TimeSpan timeout)
    {
        await cli.Process.WaitForExitAsync().WaitAsync(timeout);
        return cli.Process.ExitCode;
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    private static string Antiforgery(string html) =>
        AntiforgeryRegex().Match(html).Groups["v"].Value;

    [GeneratedRegex(@"enter code:\s*(?<code>[A-Za-z0-9-]+)")]
    private static partial Regex UserCodeRegex();

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex AntiforgeryRegex();

    private sealed class CliProcess : IDisposable
    {
        private readonly System.Text.StringBuilder _output = new();

        public CliProcess(ProcessStartInfo startInfo)
        {
            Process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the CLI.");
            Process.OutputDataReceived += (_, e) => Append(e.Data);
            Process.ErrorDataReceived += (_, e) => Append(e.Data);
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
        }

        public Process Process { get; }

        public string Output
        {
            get
            {
                lock (_output)
                {
                    return _output.ToString();
                }
            }
        }

        private void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_output)
            {
                _output.AppendLine(line);
            }
        }

        public void Dispose()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best effort
            }

            Process.Dispose();
        }
    }
}
