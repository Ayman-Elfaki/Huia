using Huia.Branding;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// <see cref="TodoApiFactory"/>'s <see cref="HuiaOptions"/> is a singleton, so — same as
/// <see cref="EmailTemplateRenderingTests"/> — tests here mutate <see cref="HuiaOptions.Branding"/> directly
/// rather than spinning up a dedicated host per case, safe because xUnit runs the <c>[Fact]</c>s within one
/// class sequentially.
/// </summary>
public class BrandingRenderingTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    [Fact]
    public async Task Login_WithBackgroundImageUrlConfigured_RendersItAsPageBackground()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<HuiaOptions>();
        options.Branding.BackgroundImageUrl = "https://example.com/bg.jpg";
        options.Branding.CustomCss = null;

        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/identity/account/login");

        Assert.Contains(".huia-page {", html, StringComparison.Ordinal);
        Assert.Contains("background-image: url(\"https://example.com/bg.jpg\")", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WithCustomCssConfigured_RendersItVerbatim()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<HuiaOptions>();
        options.Branding.BackgroundImageUrl = null;
        options.Branding.CustomCss = ".huia-panel { border-radius: 0; }";

        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/identity/account/login");

        Assert.Contains(".huia-panel { border-radius: 0; }", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WithoutBackgroundOrCustomCss_OmitsBothStyleBlocks()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<HuiaOptions>();
        options.Branding.BackgroundImageUrl = null;
        options.Branding.CustomCss = null;

        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/identity/account/login");

        Assert.DoesNotContain("background-image:", html, StringComparison.Ordinal);
        Assert.DoesNotContain(".huia-panel { border-radius: 0; }", html, StringComparison.Ordinal);
    }
}
