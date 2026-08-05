namespace Huia.TodoApi.Models;

/// <summary>
/// A single todo item, owned by the subject (user) that created it.
/// </summary>
public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The <c>sub</c> claim of the access token that created this item — a foreign key into
    /// <see cref="TodoUser"/>.
    /// </summary>
    public required string OwnerId { get; set; }

    public TodoUser? Owner { get; set; }

    public required string Title { get; set; }

    public bool IsComplete { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}