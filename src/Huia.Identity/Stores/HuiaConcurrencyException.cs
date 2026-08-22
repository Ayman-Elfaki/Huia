namespace Huia.Stores;

/// <summary>
/// Thrown by <see cref="IHuiaUserStore.UpdateAsync"/>/<see cref="IHuiaRoleStore.UpdateAsync"/> when the
/// entity's concurrency stamp no longer matches the persisted value — it was modified by another request
/// since it was loaded.
/// </summary>
public sealed class HuiaConcurrencyException : Exception
{
    /// <summary>Creates a new <see cref="HuiaConcurrencyException"/>.</summary>
    public HuiaConcurrencyException()
        : base("The entity was modified concurrently; the concurrency stamp no longer matches.")
    {
    }

    /// <summary>Creates a new <see cref="HuiaConcurrencyException"/> wrapping <paramref name="innerException"/>.</summary>
    public HuiaConcurrencyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
