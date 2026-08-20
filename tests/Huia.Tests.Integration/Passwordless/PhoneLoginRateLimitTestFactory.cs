using Huia.Identity;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Huia.Tests.Integration.Passwordless;

/// <summary>
/// Same shape as <see cref="PhoneLoginTestFactory"/>, but deliberately keeps the sample's production-default
/// OTP rate limit (1/minute, 3/hour, 10/day — <c>Program.cs</c>'s <c>huia.Authentication.UsePasswordlessFlow()</c>
/// call doesn't override it) instead of relaxing it, so tests here can actually trip the limit. Kept as a
/// separate factory/fixture rather than folded into <see cref="PhoneLoginTestFactory"/> so the two concerns
/// (rate-limit enforcement vs. everything else about the flow) don't fight over one shared limit.
/// </summary>
public sealed class PhoneLoginRateLimitTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public PhoneLoginRateLimitTestFactory()
    {
        _connection.Open();
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    private readonly List<(string PhoneNumberE164, string Code)> _sentOtpCodes = [];

    /// <summary>OTP codes captured by the fake <see cref="ISmsSender{TUser}"/>, newest last.</summary>
    public IReadOnlyList<(string PhoneNumberE164, string Code)> SentOtpCodes => _sentOtpCodes;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // See TodoApiFactory's own comment on this: pins Quartz's process-wide static log provider
            // away from any one host's disposable ILoggerFactory.
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

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
            services.AddSingleton<ISmsSender<HuiaUser>>(new CapturingSmsSender(this));
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

    private sealed class CapturingSmsSender(PhoneLoginRateLimitTestFactory factory) : ISmsSender<HuiaUser>
    {
        public Task SendOtpAsync(HuiaUser user, string phoneNumberE164, string code)
        {
            factory._sentOtpCodes.Add((phoneNumberE164, code));
            return Task.CompletedTask;
        }
    }
}
