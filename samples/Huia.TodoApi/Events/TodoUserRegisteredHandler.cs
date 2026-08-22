using Huia.Eventing;
using Huia.Identity;
using Huia.TodoApi.Data;
using Huia.TodoApi.Models;
using Microsoft.EntityFrameworkCore;

namespace Huia.TodoApi.Events;

/// <summary>
/// Gives every newly-registered Huia account a matching <see cref="TodoUser"/> row in TodoApi's own
/// database, so <see cref="TodoItem.OwnerId"/> can be a real foreign key instead of a bare, unverified
/// string. Registered as <c>IEventHandler&lt;UserRegisteredEvent&gt;</c> — see
/// <c>WebApplicationBuilderExtensions.SeedAdminAsync</c>, which publishes the same event for the seeded demo
/// admin so it goes through this one code path too.
/// </summary>
public sealed class TodoUserRegisteredHandler(TodoDbContext db, HuiaUserManager userManager)
    : IEventHandler<UserRegisteredEvent<string>>
{
    public async Task HandleAsync(UserRegisteredEvent<string> @event, CancellationToken cancellationToken = default)
    {
        var exists = await db.Users.AnyAsync(u => u.Id == @event.UserId, cancellationToken);
        if (exists) return;

        // The event itself no longer carries the email (see UserRegisteredEvent), so it's looked up here instead.
        var user = await userManager.FindByIdAsync(@event.UserId);
        db.Users.Add(new TodoUser { Id = @event.UserId, Email = user?.Email });
        await db.SaveChangesAsync(cancellationToken);
    }
}
