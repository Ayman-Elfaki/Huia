using System.Security.Claims;
using Huia;
using Huia.Headless.Endpoints;
using Huia.Headless.EntityFrameworkCore;
using Huia.Headless.EntityFrameworkCore.DependencyInjection;
using Huia.Headless.Options;
using Huia.Identity;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var issuer = builder.Configuration.GetValue("Huia:Issuer", "https://localhost:5340")!;
var shopAppUrl = builder.Configuration.GetValue("Clients:ShopApp:BaseUrl", "http://localhost:3002")!;

// SQLite in-memory / file database
var sqliteConnection = new SqliteConnection(builder.Configuration.GetConnectionString("shop") ?? "DataSource=:memory:");
sqliteConnection.Open();
builder.Services.AddSingleton(sqliteConnection);

builder.Services.AddDbContext<HuiaHeadlessDbContext>(options =>
{
    options.UseSqlite(sqliteConnection);
});

builder.Services.AddHuiaHeadlessEntityFrameworkCore<HuiaHeadlessDbContext>();

builder.Services.AddHostedService<ShopSchemaInitializer>();
builder.Services.AddHostedService<ShopSampleSeeder>();

var huiaBuilder = builder.Services.AddHuia(huia =>
{
    huia.UseIssuer(issuer);
    huia.UsePublicUrl(issuer);
    if (builder.Environment.IsDevelopment())
    {
        huia.DisableTransportSecurityRequirement();
    }

    huia.ConfigureSms(sms => sms.LogCodesToLogger = builder.Environment.IsDevelopment());

    huia.AddTenant("shop", tenant =>
    {
        tenant.Branding.DisplayName = "Huia Shop";
        tenant.Branding.AccentColor = "#0284c7";

        tenant.Authentication.UseEmailAndPasswordLogin(password =>
        {
            password.RequireConfirmedEmail = false;
            password.AllowSelfServiceRegistration = true;
        });

        tenant.Authentication.UsePhoneLogin(phone =>
        {
            phone.DefaultCountry = "US";
            phone.AllowAutoProvisioning = true;
        });

        tenant.AddHuiaHeadless(headless =>
        {
            headless.AllowedOrigins.Add(shopAppUrl);
            headless.AccessToken.Lifetime = TimeSpan.FromMinutes(15);
            headless.RefreshToken.Lifetime = TimeSpan.FromDays(30);
            headless.RefreshToken.SlidingExpiration = true;
        });
    });
}).AddHuiaHeadless();

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseHuiaHeadless();

app.MapHuiaHeadlessEndpoints();
app.MapHuiaEndpoints();

// Domain endpoints for the Shop sample
var shopApi = app.MapGroup("api");

shopApi.MapGet("products", () => Results.Ok(new[]
{
    new { id = "prod-1", name = "Huia Mechanical Keyboard", price = 149.99, inStock = true },
    new { id = "prod-2", name = "Wireless Ergonomic Mouse", price = 79.99, inStock = true },
    new { id = "prod-3", name = "4K Ultra-Wide Monitor", price = 599.99, inStock = true },
    new { id = "prod-4", name = "Desk Mat (Dark Charcoal)", price = 29.99, inStock = true },
}));

shopApi.MapGet("cart", (ClaimsPrincipal user) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    return Results.Ok(new
    {
        userId,
        items = new[]
        {
            new { productId = "prod-1", quantity = 1, unitPrice = 149.99 },
            new { productId = "prod-4", quantity = 2, unitPrice = 29.99 },
        },
        total = 209.97,
    });
}).RequireAuthorization(HuiaConstants.Policies.Api);

shopApi.MapPost("orders", (ClaimsPrincipal user) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    var orderId = $"ord-{Guid.NewGuid().ToString("N")[..8]}";
    return Results.Ok(new
    {
        orderId,
        userId,
        status = "Confirmed",
        createdAt = DateTimeOffset.UtcNow,
    });
}).RequireAuthorization(HuiaConstants.Policies.Api);

app.Run();

/// <summary>Test entry point marker.</summary>
public partial class Program;

internal sealed class ShopSchemaInitializer(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<HuiaHeadlessDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ShopSampleSeeder(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        using (HuiaTenantScope.Enter(scope.ServiceProvider, "shop"))
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
            if (await userManager.FindByEmailAsync("customer@shop.test") is null)
            {
                var customer = new HuiaUser
                {
                    TenantId = "shop",
                    UserName = "customer@shop.test",
                    Email = "customer@shop.test",
                    EmailConfirmed = true,
                    FirstName = "Sam",
                    LastName = "Shopper",
                };
                await userManager.CreateAsync(customer, "Password1!2345");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
