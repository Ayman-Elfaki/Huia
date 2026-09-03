using Microsoft.Playwright;

namespace Huia.E2ETests;

/// <summary>A headless Chromium page with its own Playwright + browser, disposed together.</summary>
internal sealed class BrowserSession : IAsyncDisposable
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public IPage Page { get; private set; } = null!;

    public static async Task<BrowserSession> StartAsync()
    {
        var session = new BrowserSession
        {
            _playwright = await Playwright.CreateAsync(),
        };
        session._browser = await session._playwright.Chromium.LaunchAsync(new() { Headless = true });
        var context = await session._browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        session.Page = await context.NewPageAsync();
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }
}
