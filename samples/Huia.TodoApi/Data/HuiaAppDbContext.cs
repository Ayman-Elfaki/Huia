using Huia.EntityFrameworkCore.Common;
using Microsoft.EntityFrameworkCore;

namespace Huia.TodoApi.Data;

/// <summary>
/// Huia's own tables (ASP.NET Core Identity, OpenIddict, key management), namespaced under the "huia"
/// schema so they share one physical database with <see cref="TodoDbContext"/>'s "todos" schema without
/// name collisions — see Program.cs for the shared connection string.
/// </summary>
public sealed class HuiaAppDbContext(DbContextOptions<HuiaAppDbContext> options) : HuiaDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("huia");
    }
}