using System.Net.Http.Json;
using Huia.AspNetCore;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Huia.IntegrationTests;

public sealed class LocalizationTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await HuiaTestHost.StartAsync(configureEndpoints: endpoints =>
        {
            endpoints.MapGet("/greeting", (IStringLocalizer<SharedResource> localizer) =>
                Results.Ok(new GreetingResult(localizer["Diagnostics.Greeting"])));
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_default_culture_is_english()
    {
        var result = await _host.Client.GetFromJsonAsync<GreetingResult>("/acme/greeting");
        result!.Text.ShouldBe("Hello");
    }

    [Fact]
    public async Task The_culture_query_string_switches_the_resource_set_to_arabic()
    {
        var result = await _host.Client.GetFromJsonAsync<GreetingResult>("/acme/greeting?culture=ar&ui-culture=ar");
        result!.Text.ShouldBe("مرحبا");
    }

    [Fact]
    public async Task The_ui_locales_authorize_hint_switches_the_resource_set_and_persists_a_cookie()
    {
        var response = await _host.Client.GetAsync("/acme/greeting?ui_locales=fr-FR%20ar%20en");

        (await response.Content.ReadFromJsonAsync<GreetingResult>())!.Text.ShouldBe("مرحبا");
        response.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        cookies!.ShouldContain(c => c.Contains(".AspNetCore.Culture=", StringComparison.Ordinal) && c.Contains("ar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unsupported_ui_locales_hint_falls_back_to_the_default_culture()
    {
        var result = await _host.Client.GetFromJsonAsync<GreetingResult>("/acme/greeting?ui_locales=fr-FR%20de");
        result!.Text.ShouldBe("Hello");
    }

    private sealed record GreetingResult(string Text);
}
