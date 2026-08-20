using System.Globalization;
using Huia.Branding;
using Huia.Emails;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// <see cref="TodoApiFactory"/>'s <see cref="HuiaOptions"/> is a singleton, so tests here mutate
/// <see cref="HuiaOptions.Branding"/> directly rather than spinning up a dedicated host per theme
/// — safe because xUnit runs the <c>[Fact]</c>s within one class sequentially (never in parallel with each
/// other), and each test sets the branding/culture it needs up front rather than depending on what a
/// previous test left behind.
/// </summary>
public class EmailTemplateRenderingTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    [Fact]
    public async Task ConfirmationLink_RendersBrandedHtmlWithButtonUrl()
    {
        using var scope = factory.Services.CreateScope();
        var template = scope.ServiceProvider.GetRequiredService<HuiaEmailTemplate>();

        var html = await template.ConfirmationLink("https://example.com/confirm?token=abc");

        Assert.Contains("Todo", html, StringComparison.Ordinal);
        Assert.Contains("https://example.com/confirm?token=abc", html, StringComparison.Ordinal);
        Assert.Contains("Confirm your email", html, StringComparison.Ordinal);
        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordResetCode_RendersCodeInsteadOfButton()
    {
        using var scope = factory.Services.CreateScope();
        var template = scope.ServiceProvider.GetRequiredService<HuiaEmailTemplate>();

        var html = await template.PasswordResetCode("123456");

        Assert.Contains("123456", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a href", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The confirmation link's URL used to also appear a second time in a "copy and paste this link" fallback
    /// paragraph underneath the button — removed as visual/textual clutter (and because a plain-text
    /// confirmation/reset link sitting in a paragraph is more casually copy-pasteable/screenshottable than
    /// one behind a button). The button's own <c>href</c> is the only place the URL should appear now.
    /// </summary>
    [Fact]
    public async Task ConfirmationLink_DoesNotRenderACopyLinkFallbackParagraph()
    {
        using var scope = factory.Services.CreateScope();
        var template = scope.ServiceProvider.GetRequiredService<HuiaEmailTemplate>();

        var html = await template.ConfirmationLink("https://example.com/confirm?token=abc");

        Assert.DoesNotContain("copy and paste this link", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(html, "https://example.com/confirm?token=abc"));
    }

    /// <summary>
    /// Emails must render in whatever language was current when they were composed — the same
    /// <c>CultureInfo.CurrentUICulture</c>-driven <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>
    /// mechanism Huia.UI's pages use (see <c>AuthorizationEndpoints.ApplyUiLocales</c> for how a
    /// client's chosen language reaches that culture in the first place), not always the default/English one.
    /// </summary>
    [Fact]
    public async Task ConfirmationLink_RendersInTheCurrentUiCulture()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar");

            using var scope = factory.Services.CreateScope();
            var template = scope.ServiceProvider.GetRequiredService<HuiaEmailTemplate>();
            var html = await template.ConfirmationLink("https://example.com/confirm?token=abc");

            Assert.Contains("تأكيد بريدك الإلكتروني", html, StringComparison.Ordinal);
            Assert.Contains("dir=\"rtl\"", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task ConfirmationLink_WithSystemTheme_RendersLightColorsInlineWithADarkModeMediaQueryOverride()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<HuiaOptions>();
        options.Branding.DefaultTheme = BrandingOptions.ThemeMode.System;

        var template = scope.ServiceProvider.GetRequiredService<HuiaEmailTemplate>();
        var html = await template.ConfirmationLink("https://example.com/confirm?token=abc");

        Assert.Contains("background-color:#ffffff", html, StringComparison.Ordinal);
        Assert.Contains("prefers-color-scheme: dark", html, StringComparison.Ordinal);
        Assert.Contains("#18181b", html, StringComparison.Ordinal); // the dark card background, inside the media-query override
    }

    [Fact]
    public async Task ConfirmationLink_WithDarkTheme_RendersDarkColorsInlineWithNoMediaQuery()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<HuiaOptions>();
        options.Branding.DefaultTheme = BrandingOptions.ThemeMode.Dark;

        var template = scope.ServiceProvider.GetRequiredService<HuiaEmailTemplate>();
        var html = await template.ConfirmationLink("https://example.com/confirm?token=abc");

        Assert.Contains("background-color:#18181b", html, StringComparison.Ordinal);
        Assert.DoesNotContain("prefers-color-scheme", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmationLink_WithLightTheme_RendersLightColorsInlineWithNoMediaQuery()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<HuiaOptions>();
        options.Branding.DefaultTheme = BrandingOptions.ThemeMode.Light;

        var template = scope.ServiceProvider.GetRequiredService<HuiaEmailTemplate>();
        var html = await template.ConfirmationLink("https://example.com/confirm?token=abc");

        Assert.Contains("background-color:#ffffff", html, StringComparison.Ordinal);
        Assert.DoesNotContain("prefers-color-scheme", html, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;
}