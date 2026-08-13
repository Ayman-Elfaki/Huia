using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Huia.Common;
using Huia.Identity;
using Huia.TodoApi.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Huia.Tests.Integration.ExternalLogin;

/// <summary>
/// Hosts the sample <c>Huia.TodoApi</c> the same way <see cref="TodoApiFactory"/> does (private in-memory
/// SQLite, captured emails instead of real SMTP), plus registers a generic OAuth2 provider named
/// <see cref="ProviderScheme"/> pointed at an in-process <see cref="FakeIdentityProvider"/> instead of a real
/// external IdP — for driving Huia's external-login sign-in/registration/link flows end to end without
/// network access. Deliberately doesn't set <c>SignInScheme</c> on the provider: that's the regression test
/// for <c>HuiaOptions</c> defaulting <c>DefaultSignInScheme</c> to <c>IdentityConstants.ExternalScheme</c>
/// (see its own doc comment) — if that default ever regressed back to <c>ApplicationScheme</c>, this
/// provider would sign straight into the main application cookie unvalidated, and
/// <c>SignInManager.GetExternalLoginInfoAsync()</c> would come back null in every test here.
/// </summary>
public sealed class ExternalLoginTestFactory : WebApplicationFactory<Program>
{
    public const string ProviderScheme = "test-provider";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public FakeIdentityProvider FakeIdentityProvider { get; } = new();

    public ExternalLoginTestFactory()
    {
        _connection.Open();
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            var sqliteServices = new ServiceCollection().AddEntityFrameworkSqlite().BuildServiceProvider();

            services.RemoveAll<IDbContextOptionsConfiguration<HuiaAppDbContext>>();
            services.RemoveAll<DbContextOptions<HuiaAppDbContext>>();
            services.AddDbContext<HuiaAppDbContext>(options =>
                options.UseSqlite(_connection, sqlite => sqlite.MigrationsHistoryTable("__HuiaMigrationsHistory"))
                    .UseInternalServiceProvider(sqliteServices)
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

            services.RemoveAll<IDbContextOptionsConfiguration<TodoDbContext>>();
            services.RemoveAll<DbContextOptions<TodoDbContext>>();
            services.AddDbContext<TodoDbContext>(options =>
                options.UseSqlite(_connection, sqlite => sqlite.MigrationsHistoryTable("__TodoMigrationsHistory"))
                    .UseInternalServiceProvider(sqliteServices)
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

            services.RemoveAll<IEmailSender<HuiaUser>>();
            services.AddSingleton<IEmailSender<HuiaUser>>(new CapturingEmailSender(this));

            RegisterFakeProvider(services);
        });
    }

    private void RegisterFakeProvider(IServiceCollection services)
    {
        services.AddAuthentication().AddOAuth(ProviderScheme, ProviderScheme, oauth =>
        {
            oauth.ClientId = "test-client";
            oauth.ClientSecret = "test-secret";
            oauth.CallbackPath = "/signin-test-provider";

            oauth.AuthorizationEndpoint = "https://fake-idp.example/authorize";
            oauth.TokenEndpoint = "https://fake-idp.example/token";
            oauth.UserInformationEndpoint = "https://fake-idp.example/userinfo";
            oauth.BackchannelHttpHandler = FakeIdentityProvider.CreateBackchannelHandler();

            oauth.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "sub");
            oauth.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
            oauth.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "given_name");
            oauth.ClaimActions.MapJsonKey(ClaimTypes.Surname, "family_name");
            oauth.ClaimActions.MapJsonKey("email_verified", "email_verified");
            oauth.ClaimActions.MapJsonKey("picture", "picture");

            // AddOAuth's generic OAuthHandler, unlike AddGoogle/AddMicrosoftAccount, doesn't fetch
            // UserInformationEndpoint on its own — every generic-OAuth consumer has to do this itself.
            oauth.Events.OnCreatingTicket = async context =>
            {
                using var request =
                    new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                using var response = await context.Backchannel.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                using var user = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                context.RunClaimActions(user.RootElement);
            };

            // Without this, a provider-side failure (the user denying consent, etc.) throws instead of
            // reaching ExternalLoginModel.OnGetCallbackAsync's own remoteError handling — the same
            // Events.OnRemoteFailure wiring docs/external-providers.md recommends every real registration
            // include.
            oauth.Events.OnRemoteFailure = context =>
            {
                var redirectUri = QueryHelpers.AddQueryString("/identity/account/externallogin",
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["handler"] = "Callback",
                        ["remoteError"] = context.Failure?.Message ?? "unknown_error",
                    });
                context.Response.Redirect(redirectUri);
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
            FakeIdentityProvider.Dispose();
        }
    }

    private readonly List<(string Email, string ConfirmationLink)> _sentConfirmationLinks = [];

    /// <summary>Confirmation links captured by the fake <see cref="IEmailSender{TUser}"/>, newest last.</summary>
    public IReadOnlyList<(string Email, string ConfirmationLink)> SentConfirmationLinks => _sentConfirmationLinks;

    private sealed class CapturingEmailSender(ExternalLoginTestFactory factory) : IEmailSender<HuiaUser>
    {
        public Task SendConfirmationLinkAsync(HuiaUser user, string email, string confirmationLink)
        {
            factory._sentConfirmationLinks.Add((email, confirmationLink));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetLinkAsync(HuiaUser user, string email, string resetLink) => Task.CompletedTask;

        public Task SendPasswordResetCodeAsync(HuiaUser user, string email, string resetCode) => Task.CompletedTask;
    }
}
