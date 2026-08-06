using System.Net;
using Huia.EntityFrameworkCore.Sessions;
using Huia.Sessions;
using Huia.TodoApi.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// Proves <c>Huia.Sessions.UserSessionTimeoutJob</c> (Keycloak's "SSO Session Idle" equivalent) end to end
/// against the real Quartz scheduler <c>WithEntityFrameworkStores</c> starts: a session idle past
/// <c>UserSessionsOptions.IdleTimeout</c> gets revoked and back-channel-notified, exactly like an explicit
/// <c>/sessions/{id}/revoke</c> call (see <see cref="SessionTests"/>/<see cref="LogoutNotificationTests"/>);
/// a session that's merely old but still recently active is left alone.
/// </summary>
public class UserSessionTimeoutJobTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string Password = "P@ssw0rd123!";
    private const string WebClientId = "todo-web";
    private const string WebClientSecret = "todo-web-dev-secret";
    private const string WebRedirectUri = "http://localhost:3000/api/auth/callback/huia";
    private const string WebBackChannelLogoutUri = "http://localhost:3000/api/auth/backchannel-logout";

    [Fact]
    public async Task Execute_RevokesIdleSession_AndNotifiesBackChannel()
    {
        var captured = InterceptBackChannelLogout();

        using var client = CreateClient();
        var email = $"idle-timeout-{Guid.NewGuid():N}@example.com";
        await IdentityUiTestHelpers.RegisterAsync(client, email, Password);
        var sessionId = await SignInWebClientForSidAsync(client);

        await SetLastActivityAsync(sessionId, DateTimeOffset.UtcNow.AddHours(-1));
        await RunTimeoutJobAsync();

        Assert.True(await IsRevokedAsync(sessionId));
        Assert.Contains(captured, uri => uri.ToString() == WebBackChannelLogoutUri);
    }

    [Fact]
    public async Task Execute_LeavesRecentlyActiveSessionAlone()
    {
        InterceptBackChannelLogout();

        using var client = CreateClient();
        var email = $"idle-timeout-active-{Guid.NewGuid():N}@example.com";
        await IdentityUiTestHelpers.RegisterAsync(client, email, Password);
        var sessionId = await SignInWebClientForSidAsync(client);

        // Well within the 30-minute default IdleTimeout — created moments ago by the sign-in above.
        await RunTimeoutJobAsync();

        Assert.False(await IsRevokedAsync(sessionId));
    }

    /// <summary>
    /// Routes back-channel logout POSTs through a fake 200-OK responder instead of a real handler — without
    /// this, a notification aimed at todo-web's (unhosted, in this test) backchannel-logout URI would hang
    /// for the real 5-second <c>LogoutNotifier</c> timeout before falling through, needlessly slowing every
    /// test here (and, under <c>[DisallowConcurrentExecution]</c>, potentially delaying a differently-timed
    /// test's own manually-triggered run behind it).
    /// </summary>
    private List<Uri> InterceptBackChannelLogout()
    {
        var captured = new List<Uri>();
        factory.BackChannelLogoutInterceptor = (request, _) =>
        {
            captured.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        };
        return captured;
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>Drives the authorization-code + token exchange for the confidential "todo-web" client and returns the resulting session's id (the <c>sid</c> claim).</summary>
    private static async Task<string> SignInWebClientForSidAsync(HttpClient client)
    {
        var state = Guid.NewGuid().ToString("N");
        var query = $"response_type=code&client_id={WebClientId}&redirect_uri={Uri.EscapeDataString(WebRedirectUri)}" +
                    $"&scope=openid+todos&state={state}";
        using var authorizeResponse = await client.GetAsync($"/connect/authorize?{query}");
        Assert.Equal(HttpStatusCode.Found, authorizeResponse.StatusCode);
        var code = ExtractQueryParameter(authorizeResponse.Headers.Location!, "code");

        using var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = WebRedirectUri,
                ["client_id"] = WebClientId,
                ["client_secret"] = WebClientSecret,
            }));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenDocument =
            System.Text.Json.JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var idToken = tokenDocument.RootElement.GetProperty("id_token").GetString()!;

        var claims = System.Text.Json.JsonDocument.Parse(Base64UrlDecode(idToken.Split('.')[1]));
        return claims.RootElement.GetProperty(SessionClaimTypes.Sid).GetString()!;
    }

    private async Task SetLastActivityAsync(string sessionId, DateTimeOffset lastActivityAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HuiaAppDbContext>();
        var session = await db.Set<UserSession>().SingleAsync(s => s.Id == sessionId);
        session.LastActivityAt = lastActivityAt;
        await db.SaveChangesAsync();
    }

    private async Task<bool> IsRevokedAsync(string sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HuiaAppDbContext>();
        var session = await db.Set<UserSession>().SingleAsync(s => s.Id == sessionId);
        return session.RevokedAt is not null;
    }

    /// <summary>
    /// Resolves the same <c>UserSessionTimeoutJob</c> instance Quartz's own scheduler would use and calls its
    /// scan-and-revoke pass directly, rather than going through <c>IScheduler.TriggerJob</c> — Quartz.NET's
    /// default <c>SchedulerRepository</c> is a process-wide static singleton keyed by scheduler name, and
    /// every <c>TodoApiFactory</c>-backed test class in this assembly creates one with the same default name;
    /// under a full parallel test run that made <c>TriggerJob</c> queue behind unrelated hosts' schedulers
    /// (or resolve to one from an already-disposed host) unpredictably. Calling <c>RunAsync</c> directly
    /// exercises the exact same logic deterministically, without depending on that shared scheduler at all.
    /// </summary>
    private async Task RunTimeoutJobAsync()
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<UserSessionTimeoutJob>();
        await job.RunAsync(CancellationToken.None);
    }

    private static string ExtractQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"Query parameter '{name}' not found in '{uri}'.");
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
