using Huia.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.IntegrationTests;

public sealed class CookieAndSecurityHeaderTests
{
    [Fact]
    public async Task Security_headers_are_absent_until_opted_in()
    {
        await using var host = await HuiaTestHost.StartAsync();

        var response = await host.Client.GetAsync("/acme/probe");

        response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
    }

    [Fact]
    public async Task Opting_in_emits_a_nonce_based_content_security_policy_and_companions()
    {
        await using var host = await HuiaTestHost.StartAsync(
            configureBuilder: builder => builder.AddHuiaSecurityHeaders());

        var response = await host.Client.GetAsync("/acme/probe");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'self'");
        csp.ShouldContain("script-src 'self' 'nonce-");
        csp.ShouldContain("style-src 'self' 'nonce-");
        csp.ShouldContain("frame-ancestors 'none'");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
    }

    [Fact]
    public async Task Disabling_scripts_locks_script_src_to_none()
    {
        await using var host = await HuiaTestHost.StartAsync(
            configureBuilder: builder => builder.AddHuiaSecurityHeaders(options => options.DisableScripts = true));

        var response = await host.Client.GetAsync("/acme/probe");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        csp.ShouldContain("script-src 'none'");
    }

    [Fact]
    public async Task The_antiforgery_cookie_is_named_and_hardened()
    {
        await using var host = await HuiaTestHost.StartAsync(configureEndpoints: endpoints =>
        {
            endpoints.MapGet("/issue-csrf", (IAntiforgery antiforgery, HttpContext context) =>
            {
                antiforgery.GetAndStoreTokens(context);
                return Results.Ok();
            });
        });

        var response = await host.Client.GetAsync("/acme/issue-csrf");
        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];

        setCookie.ShouldContain(c => c.StartsWith("huia.csrf=", StringComparison.Ordinal));
        setCookie.First(c => c.StartsWith("huia.csrf=", StringComparison.Ordinal))
            .ToLowerInvariant().ShouldContain("samesite=strict");
    }
}
