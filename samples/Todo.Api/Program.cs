using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Todo.Api;

var builder = WebApplication.CreateBuilder(args);

var huiaBaseUrl = builder.Configuration.GetValue("Huia:BaseUrl", "https://localhost:5310")!;
var authority = $"{huiaBaseUrl}/todo";

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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "role";
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<TodoDbContext>().Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();

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
