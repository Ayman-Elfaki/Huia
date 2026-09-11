using Huia.Emails;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Services;
using Microsoft.AspNetCore.Identity;

namespace Huia.IdentityServer;

/// <summary>Seeds the demo accounts once the schema and clients are in place.</summary>
internal sealed class HuiaSampleSeeder(
    IServiceProvider services, IConfiguration configuration, ILogger<HuiaSampleSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var enableE2E = configuration.GetValue("Huia:EnableE2E", false);

        await SeedAdminAsync();
        // The todo tenant sets a 12-char minimum + a required symbol (per-tenant IdentityOptions).
        await SeedUserAsync("todo", "alice@todo.test", "Password1!2345", "Alice", "Anderson");
        // Demonstrates a user with several roles — the roles claim carries an array, not a scalar.
        await AssignRolesAsync("todo", "alice@todo.test", "editor", "beta-tester");
        // Shares an email with a partner IdP user, so an external sign-in links to this account
        // (the todo tenant enables LinkExistingAccountsByEmail).
        await SeedUserAsync("todo", "link@partners.test", "Password1!2345", "Linus", "Existing");

        if (enableE2E)
        {
            await SeedUserAsync("e2e", "e2e-user@huia.local", "Password1!", "Eve", "Everett");
            await SeedUserAsync("e2e", "e2e-phone@huia.local", password: null, "Phoebe", "Nguyen", "+15005550006");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // A fresh DI scope per tenant, with the tenant entered BEFORE the Identity managers (and hence
    // HuiaDbContext) are resolved — HuiaDbContext snapshots its tenant at construction.
    private async Task InTenantAsync(string tenantId, Func<IServiceProvider, Task> work)
    {
        await using var scope = services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, tenantId))
        {
            await work(scope.ServiceProvider);
        }
    }

    private Task SeedAdminAsync() => InTenantAsync("master", async sp =>
    {
        var roleManager = sp.GetRequiredService<RoleManager<HuiaRole>>();
        var userManager = sp.GetRequiredService<UserManager<HuiaUser>>();

        if (!await roleManager.RoleExistsAsync(HuiaConstants.Roles.Administrator))
        {
            await roleManager.CreateAsync(new HuiaRole(HuiaConstants.Roles.Administrator) { TenantId = "master" });
        }

        if (await userManager.FindByNameAsync("admin@huia.local") is null)
        {
            var admin = new HuiaUser
            {
                TenantId = "master",
                UserName = "admin@huia.local",
                Email = "admin@huia.local",
                EmailConfirmed = true,
                FirstName = "Ada",
                LastName = "Admin",
            };
            await userManager.CreateAsync(admin, "Admin1!Pass");
            await userManager.AddToRoleAsync(admin, HuiaConstants.Roles.Administrator);
        }
    });

    private Task SeedUserAsync(
        string tenantId, string email, string? password, string firstName, string lastName, string? phone = null) =>
        InTenantAsync(tenantId, async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<HuiaUser>>();
            if (await userManager.FindByNameAsync(phone ?? email) is not null)
            {
                return;
            }

            var user = new HuiaUser
            {
                TenantId = tenantId,
                UserName = phone ?? email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = phone,
                PhoneNumberConfirmed = phone is not null,
            };

            var result = password is null
                ? await userManager.CreateAsync(user)
                : await userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Seeding '{email}' in tenant '{tenantId}' failed: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            logger.LogInformation("Seeded sample user {Email} in tenant {Tenant}.", email, tenantId);
        });

    /// <summary>
    /// Assigns the user (by username) to each role. The roles themselves are seeded declaratively via
    /// <see cref="Huia.Options.TenantOptions.AddRoles"/> (see Program.cs), which runs before this
    /// hosted service (registration order) — so by the time this runs, they already exist.
    /// </summary>
    private Task AssignRolesAsync(string tenantId, string userName, params string[] roles) =>
        InTenantAsync(tenantId, async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<HuiaUser>>();

            var user = await userManager.FindByNameAsync(userName);
            if (user is null)
            {
                return;
            }

            foreach (var role in roles)
            {
                if (!await userManager.IsInRoleAsync(user, role))
                {
                    await userManager.AddToRoleAsync(user, role);
                }
            }
        });
}

/// <summary>
/// Records the last SMS code per number so the <c>/e2e-otp</c> endpoint can hand it back, and — since
/// this sender only runs in Development / E2E — writes the plaintext code to the log so you can complete
/// a phone sign-in without a real SMS provider.
/// </summary>
internal sealed partial class CapturingSmsSender(ILogger<CapturingSmsSender> logger) : ISmsSender
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _codes = new(StringComparer.Ordinal);

    public bool IsConfigured => true;

    public Task<bool> SendOtpAsync(string tenantId, string phoneNumber, string code, CancellationToken cancellationToken = default)
    {
        _codes[phoneNumber] = code;
        LogCode(tenantId, phoneNumber, code);
        return Task.FromResult(true);
    }

    public string? LastCode(string phoneNumber) => _codes.TryGetValue(phoneNumber, out var code) ? code : null;

    [LoggerMessage(LogLevel.Information, "[dev] SMS OTP for tenant {TenantId} to {PhoneNumber}: {Code}")]
    private partial void LogCode(string tenantId, string phoneNumber, string code);
}

/// <summary>Records the last action URL per email address so the <c>/e2e-mail</c> endpoint can hand it back.</summary>
internal sealed class CapturingEmailSender : IHuiaEmailSender
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _urls = new(StringComparer.OrdinalIgnoreCase);

    public Task SendEmailConfirmationAsync(HuiaUser user, string confirmationUrl, CancellationToken cancellationToken = default) =>
        Capture(user.Email, confirmationUrl);

    public Task SendPasswordResetAsync(HuiaUser user, string resetUrl, CancellationToken cancellationToken = default) =>
        Capture(user.Email, resetUrl);

    public string? LastUrl(string email) => _urls.TryGetValue(email, out var url) ? url : null;

    private Task Capture(string? email, string url)
    {
        if (!string.IsNullOrEmpty(email))
        {
            _urls[email] = url;
        }

        return Task.CompletedTask;
    }
}
