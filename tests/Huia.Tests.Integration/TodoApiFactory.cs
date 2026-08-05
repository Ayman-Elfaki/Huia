using Huia.Common;
using Huia.Identity;
using Huia.TodoApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Server;

namespace Huia.Tests.Integration;

/// <summary>
/// Hosts the sample <c>Huia.TodoApi</c> in-process against a private SQLite in-memory database (one
/// per factory instance, torn down with it) instead of the sample's own file-based one, so test runs don't
/// share or pollute state.
/// </summary>
public sealed class TodoApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TodoApiFactory()
    {
        _connection.Open();

        // OpenIddict's connect endpoints (and the discovery document) reject plain HTTP by default; TestServer
        // reports whatever scheme the client's base address uses, so requesting https here satisfies that
        // without needing AllowInsecureHttp() in the sample itself.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    /// <summary>
    /// When set, back-channel logout POSTs (see <see cref="LogoutNotifier"/>) are routed through this
    /// delegate instead of a real <see cref="HttpMessageHandler"/> — lets tests capture/inspect those
    /// requests without spinning up a second in-process host per test (which raced against this factory's
    /// own Quartz-registered logger provider being torn down; see LogoutNotificationTests).
    /// Left <see langword="null"/>, requests fall through to a real handler, which is what every other test
    /// gets by default since none of them exercise back-channel logout.
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? BackChannelLogoutInterceptor
    {
        get;
        set;
    }

    /// <summary>Confirmation links captured by the fake <see cref="IEmailSender{TUser}"/>,
    /// newest last — for tests that need to inspect what Huia would have emailed
    /// (e.g. its <c>returnUrl</c>) without a real SMTP sender.
    /// </summary>
    public List<(string Email, string ConfirmationLink)> SentConfirmationLinks { get; } = [];

    /// <summary>Reset links captured by the fake <see cref="IEmailSender{TUser}"/>, newest last.</summary>
    public List<(string Email, string ResetLink)> SentPasswordResetLinks { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // The sample's own Program.cs already configured these two contexts for Npgsql. AddDbContext's
            // options-configuration is composable, not last-one-wins — EF Core merges every registered
            // IDbContextOptionsConfiguration<T> when it builds the final DbContextOptions<T>, so on top of
            // removing DbContextOptions<T> itself, that composable configuration entry has to go too, or the
            // rebuilt options end up with both UseNpgsql and UseSqlite applied to the same builder ("Multiple
            // relational database provider configurations found"). A private, SQLite-only internal service
            // provider then keeps these two contexts from also colliding with Npgsql's provider services
            // still sitting in the app's own container.
            var sqliteServices = new ServiceCollection().AddEntityFrameworkSqlite().BuildServiceProvider();

            // Both contexts share this one in-memory connection, same as the sample shares one physical
            // SQLite file — so each needs the same distinct migrations-history table name Program.cs gives
            // it, or the second context's migrations would look already-applied via the first's history row.
            services.RemoveAll<IDbContextOptionsConfiguration<HuiaAppDbContext>>();
            services.RemoveAll<DbContextOptions<HuiaAppDbContext>>();
            services.AddDbContext<HuiaAppDbContext>(options =>
                options.UseSqlite(_connection, sqlite => sqlite.MigrationsHistoryTable("__HuiaMigrationsHistory"))
                    .UseInternalServiceProvider(sqliteServices)
                    // The sample's migrations are generated against Npgsql (see Data/Migrations); the model
                    // snapshot they carry reflects Npgsql's own type mappings/annotations, which will never
                    // match what this same C# model computes under SQLite — a permanent, harmless mismatch
                    // for this provider swap, not a real pending migration.
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

            services.RemoveAll<IDbContextOptionsConfiguration<TodoDbContext>>();
            services.RemoveAll<DbContextOptions<TodoDbContext>>();
            services.AddDbContext<TodoDbContext>(options =>
                options.UseSqlite(_connection, sqlite => sqlite.MigrationsHistoryTable("__TodoMigrationsHistory"))
                    .UseInternalServiceProvider(sqliteServices)
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

            // The sample registers SmtpEmailSender, which needs a real Mailpit connection string (only
            // present under the Aspire AppHost); swap it for a fake that just records what it was asked to
            // send, so registration/password-reset flows can be exercised here without one, and tests that
            // care can inspect SentConfirmationLinks/SentPasswordResetLinks.
            services.RemoveAll<IEmailSender<HuiaUser>>();
            services.AddSingleton<IEmailSender<HuiaUser>>(new CapturingEmailSender(this));

            // OpenIddict's default 30-second RefreshTokenReuseLeeway exists to tolerate a client firing
            // concurrent refresh requests; no test here relies on that grace window, so zeroing it lets
            // TokenSecurityTests prove reuse of an already-rotated refresh token is rejected without a
            // real 30-second sleep.
            services.Configure<OpenIddictServerOptions>(options => options.RefreshTokenReuseLeeway = TimeSpan.Zero);

            services.AddHttpClient(LogoutNotifier.BackChannelHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new BackChannelLogoutRelayHandler(this));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }

    /// <summary>Forwards to <see cref="BackChannelLogoutInterceptor"/> if a test has set one, otherwise behaves like a normal handler.</summary>
    private sealed class BackChannelLogoutRelayHandler(TodoApiFactory factory)
        : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            factory.BackChannelLogoutInterceptor is { } interceptor
                ? interceptor(request, cancellationToken)
                : base.SendAsync(request, cancellationToken);
    }

    /// <summary>Records what Huia would have emailed into <see cref="SentConfirmationLinks"/>/<see cref="SentPasswordResetLinks"/> instead of actually sending anything.</summary>
    private sealed class CapturingEmailSender(TodoApiFactory factory) : IEmailSender<HuiaUser>
    {
        public Task SendConfirmationLinkAsync(HuiaUser user, string email, string confirmationLink)
        {
            factory.SentConfirmationLinks.Add((email, confirmationLink));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetLinkAsync(HuiaUser user, string email, string resetLink)
        {
            factory.SentPasswordResetLinks.Add((email, resetLink));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(HuiaUser user, string email, string resetCode) => Task.CompletedTask;
    }
}