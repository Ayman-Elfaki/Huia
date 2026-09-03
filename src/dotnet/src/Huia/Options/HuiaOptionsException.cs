namespace Huia.Options;

/// <summary>
/// Thrown when the Huia options tree fails validation. <see cref="Errors"/> holds every problem found,
/// each prefixed with the configuration path it was discovered at.
/// </summary>
public sealed class HuiaOptionsException : Exception
{
    /// <summary>Creates the exception from a set of validation errors.</summary>
    /// <param name="errors">The human-readable validation errors.</param>
    public HuiaOptionsException(IReadOnlyList<string> errors)
        : base(Format(errors))
    {
        Errors = errors;
    }

    /// <summary>The individual validation errors, each prefixed with its configuration path.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string Format(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? "Huia options are invalid."
            : "Huia options are invalid:" + Environment.NewLine +
              string.Join(Environment.NewLine, errors.Select(static e => "  - " + e));
    }
}
