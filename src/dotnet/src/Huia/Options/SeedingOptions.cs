namespace Huia.Options;

/// <summary>Controls how the start-up seeders reconcile the options tree with the database.</summary>
public sealed class SeedingOptions : IHuiaOptionsSection
{
    /// <summary>
    /// When <see langword="true"/>, a role / scope / client stamped "static" that no longer appears
    /// anywhere in the current options tree — including one whose entire tenant was removed — is
    /// deleted at start-up. Off by default: turning this on means removing a line of code deletes
    /// data. A static role that still has members is skipped (logged as a warning), never force-deleted.
    /// </summary>
    public bool PruneRemovedStaticEntities { get; set; }

    void IHuiaOptionsSection.Validate(string path, List<string> errors)
    {
    }
}
