using Huia.TodoApi.Models;
using Microsoft.EntityFrameworkCore;

namespace Huia.TodoApi.Data;


/// <summary>
/// The sample's own <see cref="TodoItem"/> table, namespaced under the "todos" schema — see
/// <see cref="HuiaAppDbContext"/>, which holds Huia's own tables under "huia" in the same physical
/// database.
/// </summary>
public sealed class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    public DbSet<TodoUser> Users => Set<TodoUser>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("todos");

        builder.Entity<TodoUser>(user =>
        {
            user.Property(u => u.Id).HasMaxLength(450);
            user.Property(u => u.Email).HasMaxLength(256);
        });

        builder.Entity<TodoItem>(todo =>
        {
            todo.Property(t => t.Title).HasMaxLength(256).IsRequired();
            todo.Property(t => t.OwnerId).HasMaxLength(450).IsRequired();
            todo.HasIndex(t => t.OwnerId);

            todo.HasOne(t => t.Owner)
                .WithMany()
                .HasForeignKey(t => t.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}