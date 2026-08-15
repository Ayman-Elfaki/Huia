namespace Huia.TodoApi.Models;

/// <summary>
/// A lightweight, local projection of a Huia account, owned entirely by TodoApi's own database (schema
/// "todos") rather than joining across to Huia's own Identity tables. Populated by
/// <see cref="Events.TodoUserRegisteredHandler"/> reacting to <c>UserRegisteredEvent</c> — the pattern a
/// decoupled relying-party service uses to keep just enough of the identity provider's user record to make
/// sense of its own data (here, <see cref="TodoItem.OwnerId"/>) without depending on Huia's storage directly.
/// </summary>
public sealed class TodoUser
{
    /// <summary>
    /// Matches the Huia account's <c>sub</c> claim (<c>HuiaUser.Id</c>).
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// <see langword="null"/> for a phone-only (passwordless) account with no email on file.
    /// </summary>
    public string? Email { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
