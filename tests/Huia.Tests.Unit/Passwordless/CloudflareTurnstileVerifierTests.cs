using System.Net;
using System.Text;
using Huia.Passwordless;
using Microsoft.Extensions.Logging.Abstractions;

namespace Huia.Tests.Unit.Passwordless;

public class CloudflareTurnstileVerifierTests
{
    private static readonly TurnstileOptions Options = new("site-key", "secret-key");

    [Fact]
    public async Task VerifyAsync_NullToken_ReturnsFalseWithoutCallingCloudflare()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Should not have been called."));
        using var client = new HttpClient(handler);
        var verifier = new CloudflareTurnstileVerifier(client, Options, NullLogger<CloudflareTurnstileVerifier>.Instance);

        var result = await verifier.VerifyAsync(null, "203.0.113.1");

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_EmptyToken_ReturnsFalseWithoutCallingCloudflare()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Should not have been called."));
        using var client = new HttpClient(handler);
        var verifier = new CloudflareTurnstileVerifier(client, Options, NullLogger<CloudflareTurnstileVerifier>.Instance);

        var result = await verifier.VerifyAsync(string.Empty, "203.0.113.1");

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_CloudflareReportsSuccess_ReturnsTrue()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true}""", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler);
        var verifier = new CloudflareTurnstileVerifier(client, Options, NullLogger<CloudflareTurnstileVerifier>.Instance);

        var result = await verifier.VerifyAsync("a-real-token", "203.0.113.1");

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyAsync_CloudflareReportsFailure_ReturnsFalse()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":false,"error-codes":["invalid-input-response"]}""",
                Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler);
        var verifier = new CloudflareTurnstileVerifier(client, Options, NullLogger<CloudflareTurnstileVerifier>.Instance);

        var result = await verifier.VerifyAsync("an-expired-token", "203.0.113.1");

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_CloudflareUnreachable_ReturnsFalse()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var verifier = new CloudflareTurnstileVerifier(client, Options, NullLogger<CloudflareTurnstileVerifier>.Instance);

        var result = await verifier.VerifyAsync("a-real-token", "203.0.113.1");

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_PostsSecretKeyResponseAndRemoteIp()
    {
        FormUrlEncodedContent? capturedContent = null;
        var handler = new StubHandler((request, _) =>
        {
            capturedContent = (FormUrlEncodedContent)request.Content!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true}""", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler);
        var verifier = new CloudflareTurnstileVerifier(client, Options, NullLogger<CloudflareTurnstileVerifier>.Instance);

        await verifier.VerifyAsync("a-real-token", "203.0.113.1");

        var body = await capturedContent!.ReadAsStringAsync();
        Assert.Contains("secret=secret-key", body, StringComparison.Ordinal);
        Assert.Contains("response=a-real-token", body, StringComparison.Ordinal);
        Assert.Contains("remoteip=203.0.113.1", body, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request, cancellationToken));
    }
}
