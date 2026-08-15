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
/// Hosts the sample <c>Huia.TodoApi</c> the same way <see cref="TodoApiFactory"/> does (private in-memory
/// SQLite), plus swaps in a <see cref="CapturingSmsSender"/> instead of the sample's conditionally-registered
/// <c>TwilioSmsSender</c> (never active in tests — no <c>Twilio:AccountSid</c> configured) so passwordless
/// sign-in can be driven end to end without a real SMS provider. The sample's <c>Program.cs</c> already calls
/// <c>huia.Authentication.UsePasswordlessFlow()</c> unconditionally, so no extra flow-enablement wiring is
/// needed here — except the per-phone-number rate limit, deliberately overridden far above the 1/minute
/// production default: most tests in this factory legitimately send more than one OTP to the same test
/// number (e.g. a "returning user" test signs in twice). Actual rate-limit *enforcement* is covered instead
/// by <see cref="PhoneLoginRateLimitTestFactory"/>, which keeps the tight production default.
/// </summary>
public sealed class PhoneLoginTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public PhoneLoginTestFactory()
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

            // A plain Add (not TryAdd), registered after Program.cs's own AddHuia already ran — the last
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

    /// <summary>Records what Huia would have texted into <see cref="SentOtpCodes"/> instead of actually sending anything.</summary>
    private sealed class CapturingSmsSender(PhoneLoginTestFactory factory) : ISmsSender<HuiaUser>
    {
        public Task SendOtpAsync(HuiaUser user, string phoneNumberE164, string code)
        {
            factory._sentOtpCodes.Add((phoneNumberE164, code));
            return Task.CompletedTask;
        }
    }
}
