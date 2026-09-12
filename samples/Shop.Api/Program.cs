using System.Security.Claims;
using Huia.Headless.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.Api;

var builder = WebApplication.CreateBuilder(args);

var issuer = builder.Configuration.GetValue("Huia:Issuer", "https://localhost:5340")!;
var enableE2E = builder.Configuration.GetValue("Huia:EnableE2E", false);
var externalIssuer = builder.Configuration.GetValue("Huia:ExternalIssuer", "https://localhost:5320")!;
var shopAppUrl = builder.Configuration.GetValue("Shop:AppUrl", "http://localhost:3040")!;

// A single shared in-memory SQLite connection kept open for the process lifetime — same pattern
// Huia.IdentityServer/Huia.External use, so the schema created at start-up survives every request.
var connection = new SqliteConnection(builder.Configuration.GetConnectionString("huia") ?? "DataSource=:memory:");
connection.Open();
builder.Services.AddSingleton(connection);
builder.Services.AddDbContext<HuiaDbContext>(options => options.UseSqlite(connection));
builder.Services.AddHostedService<SchemaInitializer>();

builder.Services
    .AddHuia(huia =>
    {
        huia.UseIssuer(issuer);
        if (builder.Environment.IsDevelopment() || enableE2E)
        {
            huia.DisableTransportSecurityRequirement();
        }

        huia.AddTenant("shop", tenant =>
        {
            tenant.Branding.DisplayName = "Huia Shop";
            tenant.Authentication.UseEmailAndPasswordLogin(password => password.RequireConfirmedEmail = false);
            tenant.Authentication.UsePhoneLogin(phone => phone.AllowAutoProvisioning = true);
            // Huia.External is the same mock upstream IdP Todo.App uses via Huia.OpenId's OpenIddict
            // client — here it's consumed through the classic OpenIdConnect handler instead, since
            // Huia.Headless has no OpenIddict dependency. See samples/Huia.External/Program.cs for the
            // "shop-api" client registration this must match (client id/secret, redirect URI).
            tenant.Authentication.UseExternalLogin(ext =>
            {
                ext.AddOpenIdConnect("HuiaExternal", "shop-api", "shop-api-secret", $"{externalIssuer}/partners", p =>
                {
                    p.DisplayName = "Partner";
                    p.Scopes.Add("profile");
                    p.Scopes.Add("email");
                });
                ext.EnableAccountsLinking();
                ext.AllowReturnUrlPrefix(shopAppUrl);
            });
        });
    })
    .AddEntityFrameworkCoreStores<HuiaDbContext>()
    .AddHuiaHeadless();

if (builder.Environment.IsDevelopment() || enableE2E)
{
    // Captures rather than sends SMS codes, so e2e tests (and local dev) can read them back via
    // /e2e-otp — the same pattern Huia.IdentityServer uses.
    builder.Services.AddSingleton<CapturingSmsSender>();
    builder.Services.AddScoped<Huia.Services.ISmsSender>(sp => sp.GetRequiredService<CapturingSmsSender>());
}

builder.Services.AddSingleton<CartStore>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetValue("Shop:AppUrl", "http://localhost:3040")!)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHuiaHeadlessEndpoints();

if (enableE2E)
{
    app.MapGet("/e2e-otp", (string phone, CapturingSmsSender sms) =>
        sms.LastCode(phone) is { } code ? Results.Ok(new { phone, code }) : Results.NotFound());
}

app.MapGet("/products", () => Catalog.Products);

var cart = app.MapGroup("/cart").RequireAuthorization();

cart.MapGet("/", (HttpContext ctx, CartStore store) => Results.Ok(store.Get(Owner(ctx))));

cart.MapPost("/items", (HttpContext ctx, CartStore store, AddCartItem body) =>
{
    if (Catalog.Products.All(p => p.Id != body.ProductId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["productId"] = ["Unknown product."] });
    }

    return Results.Ok(store.AddItem(Owner(ctx), body.ProductId, body.Quantity));
});

cart.MapDelete("/items/{productId}", (HttpContext ctx, CartStore store, string productId) =>
    Results.Ok(store.RemoveItem(Owner(ctx), productId)));

app.MapPost("/checkout", (HttpContext ctx, CartStore store) =>
{
    var items = store.Get(Owner(ctx));
    if (items.Count == 0)
    {
        return Results.Conflict(new { error = "cart_empty" });
    }

    var total = items.Sum(i => i.Quantity * Catalog.Products.First(p => p.Id == i.ProductId).Price);
    store.Clear(Owner(ctx));
    return Results.Ok(new { orderId = Guid.NewGuid().ToString("N"), total });
}).RequireAuthorization();

app.Run();

static string Owner(HttpContext ctx) => ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

/// <summary>Test entry point marker for <c>WebApplicationFactory</c>.</summary>
public partial class Program;

namespace Shop.Api
{
    internal sealed record Product(string Id, string Name, decimal Price);

    internal static class Catalog
    {
        public static readonly IReadOnlyList<Product> Products =
        [
            new("mug", "Huia Mug", 12.00m),
            new("tshirt", "Huia T-Shirt", 20.00m),
            new("sticker-pack", "Huia Sticker Pack", 5.00m),
        ];
    }

    internal sealed record CartItem(string ProductId, int Quantity);

    internal sealed record AddCartItem(string ProductId, int Quantity);

    /// <summary>In-memory per-user cart. A real shop would persist this; the point here is exercising auth.</summary>
    internal sealed class CartStore
    {
        private readonly Dictionary<string, Dictionary<string, int>> _carts = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<CartItem> Get(string owner)
        {
            lock (_gate)
            {
                return _carts.TryGetValue(owner, out var items)
                    ? [.. items.Select(kv => new CartItem(kv.Key, kv.Value))]
                    : [];
            }
        }

        public IReadOnlyList<CartItem> AddItem(string owner, string productId, int quantity)
        {
            lock (_gate)
            {
                var cart = _carts.TryGetValue(owner, out var existing) ? existing : _carts[owner] = [];
                cart[productId] = Math.Max(0, cart.GetValueOrDefault(productId) + quantity);
                if (cart[productId] == 0)
                {
                    cart.Remove(productId);
                }

                return Get(owner);
            }
        }

        public IReadOnlyList<CartItem> RemoveItem(string owner, string productId)
        {
            lock (_gate)
            {
                if (_carts.TryGetValue(owner, out var cart))
                {
                    cart.Remove(productId);
                }

                return Get(owner);
            }
        }

        public void Clear(string owner)
        {
            lock (_gate)
            {
                _carts.Remove(owner);
            }
        }
    }

    internal sealed class SchemaInitializer(IServiceProvider services) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<HuiaDbContext>().Database.EnsureCreatedAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Records the last OTP per number so <c>/e2e-otp</c> can hand it back to e2e tests, instead of sending a real SMS.</summary>
    internal sealed partial class CapturingSmsSender(ILogger<CapturingSmsSender> logger) : Huia.Services.ISmsSender
    {
        private readonly Dictionary<string, string> _codes = [];
        private readonly Lock _gate = new();

        public bool IsConfigured => true;

        public Task<bool> SendOtpAsync(string tenantId, string phoneNumber, string code, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _codes[phoneNumber] = code;
            }

            LogCode(tenantId, phoneNumber, code);
            return Task.FromResult(true);
        }

        public string? LastCode(string phoneNumber)
        {
            lock (_gate)
            {
                return _codes.TryGetValue(phoneNumber, out var code) ? code : null;
            }
        }

        [LoggerMessage(LogLevel.Information, "[dev] SMS OTP for tenant {TenantId} to {PhoneNumber}: {Code}")]
        private partial void LogCode(string tenantId, string phoneNumber, string code);
    }
}
