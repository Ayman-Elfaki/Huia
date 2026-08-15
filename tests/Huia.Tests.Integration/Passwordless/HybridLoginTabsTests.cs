using Microsoft.AspNetCore.Mvc.Testing;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>
/// Covers the Basecoat UI tabs Login.cshtml renders when both <c>UseEmailAndPasswordFlow()</c> and
/// <c>UsePasswordlessFlow()</c> are enabled (the sample enables both, unconditionally, in <c>Program.cs</c>).
/// The single-flow-only rendering paths (a plain form, no tabs) are simple, mutually-exclusive `@if`
/// branches in Login.cshtml reviewed directly rather than covered by a second test host here — building one
/// would mean standing up an entirely separate minimal app just to flip that one flag, which isn't
/// proportionate to what's a straightforward, low-risk conditional.
/// </summary>
public class HybridLoginTabsTests(PhoneLoginTestFactory factory) : IClassFixture<PhoneLoginTestFactory>
{
    [Fact]
    public async Task Login_Get_WithBothFlowsEnabled_RendersBothTabsAndPanels()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/identity/account/login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("role=\"tablist\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-orientation=\"horizontal\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"huia-tab-password\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"huia-tab-phone\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"huia-tab-panel-password\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"huia-tab-panel-phone\"", html, StringComparison.Ordinal);

        // The password tab panel is the one visible by default (progressive enhancement — see Login.cshtml);
        // the phone panel is server-rendered `hidden` until the tabs script picks a tab client-side. No
        // aria-labelledby on either panel — see Login.cshtml's own comment on why that would collide with
        // the "Email" field's own <label>.
        Assert.Contains("id=\"huia-tab-panel-phone\" tabindex=\"-1\" hidden", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_Get_WithBothFlowsEnabled_TablistRendersOutsideTheCard()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/identity/account/login");
        var html = await response.Content.ReadAsStringAsync();

        // The Email/Phone switcher sits above .card rather than inside it — the tablist must appear before
        // .card opens, not after (Basecoat's tabs script resolves each panel via the tab's aria-controls id,
        // not DOM nesting, so the panels themselves can stay inside .card either way — see Login.cshtml).
        var cardIndex = html.IndexOf("class=\"card\"", StringComparison.Ordinal);
        var tablistIndex = html.IndexOf("role=\"tablist\"", StringComparison.Ordinal);
        Assert.True(cardIndex >= 0 && tablistIndex >= 0 && tablistIndex < cardIndex,
            "Expected the tablist to render before .card opens.");
    }

    [Fact]
    public async Task Login_Get_WithBothFlowsEnabled_StillListsExternalLoginFooterLinks()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/identity/account/login");
        var html = await response.Content.ReadAsStringAsync();

        // The email+password-only footer links (forgot password / create account) still render — they're
        // gated on ShowEmailPasswordTab, not on whether tabs are shown at all.
        Assert.Contains("/identity/account/forgotpassword", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/identity/account/register", html, StringComparison.OrdinalIgnoreCase);
    }
}
