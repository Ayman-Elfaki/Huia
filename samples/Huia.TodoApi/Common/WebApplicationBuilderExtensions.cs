using Huia.Eventing;
using Huia.Identity;

namespace Huia.TodoApi.Common;

public static class WebApplicationBuilderExtensions
{
    // Derives the OpenIddict issuer from the address Aspire actually assigned this process, since that's not
    // known until runtime; falls back to a fixed dev port when run standalone (outside the AppHost).
    public static string ResolveIssuer(this WebApplicationBuilder builder)
    {
        var configured = builder.Configuration["Oidc:Issuer"];
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var urls = builder.Configuration["ASPNETCORE_URLS"] ?? builder.Configuration["urls"];
        var candidates = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        return candidates.FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault()
               ?? "https://localhost:5041";
    }


    // Idempotent: creates the "Admin" role and a demo admin user (if neither already exists) so the sample has
    // someone to sign in as for the role-gated endpoints above (MapHuiaAdminEndpoints, ReportsEndpoints) without
    // requiring a manual setup step. Dev-only credentials, same spirit as the client secrets at the top of this
    // file — never seed a fixed password like this outside local development.
    public static async Task SeedAdminAsync(this IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<HuiaRoleManager>();
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new HuiaRole { Name = "Admin" });
        }

        var userManager = services.GetRequiredService<HuiaUserManager>();
        var admin = await userManager.FindByEmailAsync("admin@example.com");
        if (admin is null)
        {
            admin = new HuiaUser
            {
                UserName = "admin@example.com",
                Email = "admin@example.com",
                EmailConfirmed = true,
                FirstName = "Admin",
                LastName = "User",
            };
            await userManager.CreateAsync(admin, "Admin123!Demo");

            // Created directly via UserManager rather than the Register page, so it wouldn't otherwise raise
            // UserRegisteredEvent — publish it here so the admin account goes through the same
            // TodoUserRegisteredHandler path every other account does (see TodoUserRegisteredHandler),
            // giving it a TodoUser row too.
            var events = services.GetRequiredService<IEventPublisher>();
            await events.PublishAsync(new UserRegisteredEvent<string>(admin.Id));
        }

        if (!await userManager.IsInRoleAsync(admin, "Admin"))
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}