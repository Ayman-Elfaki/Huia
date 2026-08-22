using Huia.Common;
using Huia.Identity;
using Huia.Passwordless;
using Huia.Sms;
using Huia.TodoApi.Data;
using Microsoft.AspNetCore.Hosting;
using Huia.Emails;
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
/// Hosts the sample <c>Huia.TodoApi</c> the same way <see cref="TodoApiFactory"/> does (private in-memory
/// SQLite, a <see cref="CapturingEmailSender"/> so <c>AdminTestHelpers</c>' registration-based bearer-token
/// minting works without a real Mailpit connection), plus <see cref="PhoneLoginTestFactory"/>'s own
/// <see cref="CapturingSmsSender"/> and loosened per-phone-number rate limit — this test factory needs both:
/// a bearer-authenticated caller (via email/password registration) changing their phone number (via SMS OTP).
/// Kept as its own factory rather than adding SMS capture to <see cref="TodoApiFactory"/> directly (used by
/// many unrelated test classes) or reusing <see cref="PhoneLoginTestFactory"/> directly (which never swaps
/// <c>IEmailSender&lt;HuiaUser&gt;</c>, so <c>AdminTestHelpers</c>' registration flow would hit the sample's
/// real SMTP sender and fail) — matches this project's existing pattern of a dedicated factory per concern
/// (see <c>PhoneLoginRateLimitTestFactory</c>, <c>PhoneLoginIpRateLimitTestFactory</c>).
/// </summary>
public sealed class ManagePhoneChangeTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ManagePhoneChangeTestFactory()
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

            services.RemoveAll<IEmailSender<HuiaUser>>();
            services.AddSingleton<IEmailSender<HuiaUser>>(new CapturingEmailSender());

            services.RemoveAll<ISmsSender<HuiaUser>>();
            services.AddSingleton<ISmsSender<HuiaUser>>(new CapturingSmsSender(this));

            // A plain Add (not TryAdd), registered after Program.cs's own AddHuia already ran - the last
            // registration for a service type is what DI resolves, and IPhoneOtpRateLimiter's actual
            // instance isn't constructed until a request first needs it, well after ConfigureServices runs.
            services.AddSingleton(new PhoneOtpRateLimitOptions
            {
                RequestsPerMinute = 1000,
                RequestsPerHour = 1000,
                RequestsPerDay = 1000,
            });
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

    /// <summary>Discards confirmation/reset emails - only registration (via <c>AdminTestHelpers</c>) needs
    /// <see cref="IEmailSender{TUser}"/> here, and nothing in this factory's tests inspects what was sent.</summary>
    private sealed class CapturingEmailSender : IEmailSender<HuiaUser>
    {
        public Task SendConfirmationLinkAsync(HuiaUser user, string email, string confirmationLink) =>
            Task.CompletedTask;

        public Task SendPasswordResetLinkAsync(HuiaUser user, string email, string resetLink) => Task.CompletedTask;

        public Task SendPasswordResetCodeAsync(HuiaUser user, string email, string resetCode) => Task.CompletedTask;
    }

    /// <summary>Records what Huia would have texted into <see cref="SentOtpCodes"/> instead of actually sending anything.</summary>
    private sealed class CapturingSmsSender(ManagePhoneChangeTestFactory factory) : ISmsSender<HuiaUser>
    {
        public Task SendOtpAsync(HuiaUser user, string phoneNumberE164, string code)
        {
            factory._sentOtpCodes.Add((phoneNumberE164, code));
            return Task.CompletedTask;
        }
    }
}
