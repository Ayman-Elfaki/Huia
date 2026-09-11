namespace Huia.Options;

/// <summary>
/// Implemented by every node in the Huia options tree. Nodes append their problems to the supplied
/// error list rather than throwing, so a single validation pass can report everything wrong at once.
/// </summary>
public interface IHuiaOptionsSection
{
    /// <summary>Validates this node and its children.</summary>
    /// <param name="path">The configuration path of this node (for example <c>Huia:Tenants:acme</c>).</param>
    /// <param name="errors">The collection problems are appended to.</param>
    void Validate(string path, List<string> errors);
}

/// <summary>Helpers for consistent option-path composition and error formatting.</summary>
internal static class HuiaOptionsValidation
{
    public static string Combine(string path, string member) =>
        string.IsNullOrEmpty(path) ? member : path + ":" + member;

    public static void Require(this List<string> errors, bool condition, string path, string message)
    {
        if (!condition)
        {
            errors.Add($"{path}: {message}");
        }
    }
}
