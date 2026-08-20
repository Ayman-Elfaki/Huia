using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Huia.Common;

/// <summary>
/// Validates a person's first/last name: required, at most <see cref="MaxLength"/> characters (matching the
/// column's own <c>HasMaxLength</c> in <c>HuiaDbContext</c>), and letters (any script) plus spaces, hyphens,
/// and apostrophes only — enough for names like "Anne-Marie", "O'Brien", or "José" while rejecting
/// digits/symbols/emoji. Shared by <see cref="PersonNameAttribute"/> (the Identity UI's Register/external
/// login/phone login confirmation pages) and the admin/manage minimal API endpoints, which don't get
/// DataAnnotations validation automatically.
/// </summary>
internal static partial class PersonNameValidator
{
    public const int MaxLength = 256;

    // Bounded match timeout despite the pattern having no nested/overlapping quantifiers to actually
    // backtrack catastrophically on - satisfies static ReDoS analysis without relying on that assumption.
    [GeneratedRegex(@"^[\p{L}\s'-]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex NamePattern();

    /// <summary>
    /// True if <paramref name="name"/> is non-empty, at most <see cref="MaxLength"/> characters, and contains
    /// only letters, spaces, hyphens, and apostrophes.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= MaxLength && NamePattern().IsMatch(name);

    /// <summary>The error message for a minimal API endpoint to report against <paramref name="fieldLabel"/>
    /// (e.g. <c>"First name"</c>) when <see cref="IsValid"/> returns <see langword="false"/>.</summary>
    public static string DescribeError(string fieldLabel) =>
        $"{fieldLabel} is required, must be at most {MaxLength} characters, and may contain only letters, " +
        "spaces, hyphens, and apostrophes.";
}
