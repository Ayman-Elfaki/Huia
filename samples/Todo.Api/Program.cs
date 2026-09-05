using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenIddict.Validation.AspNetCore;
using Scalar.AspNetCore;
using Todo.Api;

var builder = WebApplication.CreateBuilder(args);

var huiaBaseUrl = builder.Configuration.GetValue("Huia:BaseUrl", "https://localhost:5310")!;
var authority = $"{huiaBaseUrl}/todo";

// Where Scalar's "Authorize" flow sends the browser back with the code — must match the redirect URI
// registered for the "todo-api-docs" client in the identity server. Scalar handles the callback on
// its own reference route.
var publicUrl = builder.Configuration.GetValue("Todo:PublicUrl", "http://localhost:5330")!;
var scalarRedirectUri = $"{publicUrl}/scalar";

var databaseProvider = builder.Configuration.GetValue("Todo:Database", "Sqlite")!;
builder.Services.AddDbContext<TodoDbContext>(options =>
{
    if (string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        var connectionString = builder.Configuration.GetConnectionString("todo")
            ?? throw new InvalidOperationException("A 'todo' connection string is required for the Postgres provider.");
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("todo") ?? "DataSource=todo.db");
    }
});

// Validate the access token against the "todo" tenant issuer. OpenIddict fetches the tenant's
// discovery document + JWKS over HTTP and verifies the JWT locally — no shared key material, so this
// works as a standalone resource server. Replaces Microsoft's JwtBearer handler.
builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(authority);
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddAuthorization();

// OpenAPI document + an OpenID Connect security scheme so the Scalar reference can drive the
// authorization-code + PKCE flow against the "todo" tenant.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["oidc"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OpenIdConnect,
        OpenIdConnectUrl = new Uri($"{authority}/.well-known/openid-configuration"),
    };
    document.Security ??= [];
    document.Security.Add(new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("oidc", document)] = [],
    });
    return Task.CompletedTask;
}));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<TodoDbContext>().Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();

// /openapi/v1.json + a Scalar reference UI at /scalar wired for OAuth "try it".
app.MapOpenApi();
app.MapScalarApiReference(options => options
    .AddPreferredSecuritySchemes("oidc")
    .AddAuthorizationCodeFlow("oidc", flow =>
    {
        flow.ClientId = "todo-api-docs";
        flow.Pkce = Pkce.Sha256;
        flow.SelectedScopes = ["openid", "profile", "email"];
        flow.WithRedirectUri(scalarRedirectUri);
    }));

var todos = app.MapGroup("/todos").RequireAuthorization();

todos.MapGet("/", async (TodoDbContext db, HttpContext ctx) =>
{
    var owner = ctx.User.FindFirst("sub")?.Value ?? string.Empty;
    var items = await db.Items.Where(t => t.Owner == owner).ToListAsync();
    // SQLite cannot ORDER BY a DateTimeOffset; sort in memory.
    return items.OrderByDescending(t => t.CreatedAt).Select(TodoDto.From);
});

todos.MapPost("/", async (TodoDbContext db, HttpContext ctx, CreateTodo body) =>
{
    var item = new TodoItem
    {
        Owner = ctx.User.FindFirst("sub")?.Value ?? string.Empty,
        Title = body.Title,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.Items.Add(item);
    await db.SaveChangesAsync();
    return Results.Created($"/todos/{item.Id}", TodoDto.From(item));
});

todos.MapPut("/{id:int}", async (TodoDbContext db, HttpContext ctx, int id, UpdateTodo body) =>
{
    var owner = ctx.User.FindFirst("sub")?.Value ?? string.Empty;
    var item = await db.Items.FirstOrDefaultAsync(t => t.Id == id && t.Owner == owner);
    if (item is null)
    {
        return Results.NotFound();
    }

    item.Title = body.Title;
    item.Done = body.Done;
    await db.SaveChangesAsync();
    return Results.Ok(TodoDto.From(item));
});

todos.MapDelete("/{id:int}", async (TodoDbContext db, HttpContext ctx, int id) =>
{
    var owner = ctx.User.FindFirst("sub")?.Value ?? string.Empty;
    var removed = await db.Items.Where(t => t.Id == id && t.Owner == owner).ExecuteDeleteAsync();
    return removed > 0 ? Results.NoContent() : Results.NotFound();
});

app.Run();

/// <summary>Test entry point marker.</summary>
public partial class Program;

namespace Todo.Api
{
    internal sealed class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
    {
        public DbSet<TodoItem> Items => Set<TodoItem>();
    }

    internal sealed class TodoItem
    {
        public int Id { get; set; }

        public string Owner { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public bool Done { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }

    internal sealed record TodoDto(int Id, string Title, bool Done, DateTimeOffset CreatedAt)
    {
        public static TodoDto From(TodoItem item) => new(item.Id, item.Title, item.Done, item.CreatedAt);
    }

    internal sealed record CreateTodo(string Title);

    internal sealed record UpdateTodo(string Title, bool Done);
}
