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
/// Same shape as <see cref="PhoneLoginTestFactory"/> (private in-memory SQLite, a capturing
/// <c>ISmsSender</c>), but additionally overrides <see cref="ITurnstileVerifier"/> with a fully controllable
/// <see cref="FakeTurnstileVerifier"/> — the sample's own <c>Program.cs</c> doesn't turn Turnstile on, so
/// proving <c>PhoneLoginModel.OnPostAsync</c> actually wires it in correctly needs it swapped in directly, the
/// same "plain AddSingleton after AddHuia already ran" override <see cref="PhoneLoginTestFactory"/> uses for
/// <c>PhoneOtpRateLimitOptions</c> — just at the service level here rather than the options level, since which
/// concrete type gets registered (the real implementation vs. a no-op) is decided by <c>Program.cs</c>'s own
/// configuration, not just the options' values. The IP rate limit is deliberately left at its (relaxed,
/// effectively unlimited) default here — enforcement is covered instead by
/// <see cref="PhoneLoginIpRateLimitTestFactory"/>, kept separate for the same reason
/// <see cref="PhoneLoginRateLimitTestFactory"/> is kept separate from this one: two concerns sharing one
/// limiter singleton across every test in a class would have each test's successful request eat into every
/// other test's budget.
/// </summary>
public sealed class PhoneLoginBotProtectionTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public PhoneLoginBotProtectionTestFactory()
    {
        _connection.Open();
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    private readonly List<(string PhoneNumberE164, string Code)> _sentOtpCodes = [];

    /// <summary>OTP codes captured by the fake <see cref="ISmsSender{TUser}"/>, newest last.</summary>
    public IReadOnlyList<(string PhoneNumberE164, string Code)> SentOtpCodes => _sentOtpCodes;

    /// <summary>Controls whether the next <see cref="ITurnstileVerifier.VerifyAsync"/> call reports success.</summary>
    public FakeTurnstileVerifier TurnstileVerifier { get; } = new();

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

            // Relaxed so it never interferes with these IP/Turnstile-focused tests — see PhoneLoginTestFactory's
            // own comment on this exact override.
            services.AddSingleton(new PhoneOtpRateLimitOptions
            {
                RequestsPerMinute = 1000,
                RequestsPerHour = 1000,
                RequestsPerDay = 1000,
            });

            // Relaxed for the same reason as the phone-number limit above — see this class's own doc comment.
            services.AddSingleton<IPhoneIpRateLimiter>(new PhoneIpRateLimiter(new PhoneIpRateLimitOptions
            {
                RequestsPerMinute = 1000,
                RequestsPerHour = 1000,
                RequestsPerDay = 1000,
            }));

            services.AddSingleton<ITurnstileVerifier>(TurnstileVerifier);
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
    private sealed class CapturingSmsSender(PhoneLoginBotProtectionTestFactory factory) : ISmsSender<HuiaUser>
    {
        public Task SendOtpAsync(HuiaUser user, string phoneNumberE164, string code)
        {
            factory._sentOtpCodes.Add((phoneNumberE164, code));
            return Task.CompletedTask;
        }
    }
}
