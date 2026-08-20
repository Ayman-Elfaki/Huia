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

namespace Huia.Tests.Integration;

/// <summary>
/// Same shape as <see cref="TodoApiFactory"/> (private in-memory SQLite, capturing SMS sender for
/// passwordless), plus flips <c>huia.DisableRegistration()</c> on after the sample's own <c>AddHuia</c> call
/// already ran. <c>HuiaOptions</c> is registered as a plain singleton *instance* (not via the
/// <c>IOptions&lt;T&gt;</c> pattern), so it's mutated in place here rather than re-registered — re-adding a
/// fresh <see cref="HuiaOptions"/> would re-run its constructor's cookie-scheme registrations a second time.
/// </summary>
public sealed class RegistrationDisabledTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public RegistrationDisabledTestFactory()
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

            var huiaOptionsDescriptor = services.Single(d => d.ServiceType == typeof(HuiaOptions));
            ((HuiaOptions)huiaOptionsDescriptor.ImplementationInstance!).DisableRegistration();
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
    private sealed class CapturingSmsSender(RegistrationDisabledTestFactory factory) : ISmsSender<HuiaUser>
    {
        public Task SendOtpAsync(HuiaUser user, string phoneNumberE164, string code)
        {
            factory._sentOtpCodes.Add((phoneNumberE164, code));
            return Task.CompletedTask;
        }
    }
}
