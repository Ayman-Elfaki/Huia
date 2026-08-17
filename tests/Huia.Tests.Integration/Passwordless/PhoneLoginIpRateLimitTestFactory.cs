using Huia.Identity;
using Huia.Passwordless;
using Huia.Sms;
using Huia.TodoApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>
/// Same shape as <see cref="PhoneLoginBotProtectionTestFactory"/>, but the other way around: a tight,
/// deterministic per-IP rate limit (1/minute) and a Turnstile verifier that always passes — kept in its own
/// factory/fixture for the same reason <see cref="PhoneLoginRateLimitTestFactory"/> is kept separate from
/// <see cref="PhoneLoginTestFactory"/>, and <see cref="PhoneLoginBotProtectionTestFactory"/> keeps its own IP
/// limit relaxed: one shared limiter singleton per test class means every test in it draws from the same
/// budget, so a class actually testing enforcement can't also host tests that expect to succeed more than
/// once.
/// </summary>
public sealed class PhoneLoginIpRateLimitTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public PhoneLoginIpRateLimitTestFactory()
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

            services.RemoveAll<ISmsSender<HuiaUser>>();
            services.AddSingleton<ISmsSender<HuiaUser>>(new NoOpCapturingSmsSender());

            // Relaxed so only the IP limit below can ever be the one rejecting a request in this factory.
            services.AddSingleton(new PhoneOtpRateLimitOptions
            {
                RequestsPerMinute = 1000,
                RequestsPerHour = 1000,
                RequestsPerDay = 1000,
            });

            services.AddSingleton<IPhoneIpRateLimiter>(new PhoneIpRateLimiter(new PhoneIpRateLimitOptions
            {
                RequestsPerMinute = 1,
                RequestsPerHour = 1000,
                RequestsPerDay = 1000,
            }));

            services.AddSingleton<ITurnstileVerifier>(new FakeTurnstileVerifier { ShouldVerify = true });
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

    private sealed class NoOpCapturingSmsSender : ISmsSender<HuiaUser>
    {
        public Task SendOtpAsync(HuiaUser user, string phoneNumberE164, string code) => Task.CompletedTask;
    }
}
