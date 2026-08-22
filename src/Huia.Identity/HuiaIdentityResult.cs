namespace Huia.Identity;

/// <summary>The result of an operation performed by <see cref="HuiaUserManager"/>/<see cref="HuiaRoleManager"/>.
/// Replaces ASP.NET Core Identity's <c>IdentityResult</c>.</summary>
public sealed class HuiaIdentityResult
{
    private static readonly IReadOnlyList<HuiaIdentityError> NoErrors = [];

    /// <summary>Whether the operation succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>The errors that caused the operation to fail — empty when <see cref="Succeeded"/> is
    /// <see langword="true"/>.</summary>
    public IReadOnlyList<HuiaIdentityError> Errors { get; }

    private HuiaIdentityResult(bool succeeded, IReadOnlyList<HuiaIdentityError> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    /// <summary>A successful result.</summary>
    public static HuiaIdentityResult Success { get; } = new(succeeded: true, NoErrors);

    /// <summary>A failed result carrying <paramref name="errors"/>.</summary>
    public static HuiaIdentityResult Failed(params HuiaIdentityError[] errors) =>
        new(succeeded: false, errors.Length == 0 ? NoErrors : errors);

    /// <summary>A failed result carrying <paramref name="errors"/>.</summary>
    public static HuiaIdentityResult Failed(IEnumerable<HuiaIdentityError> errors) =>
        new(succeeded: false, [.. errors]);

    /// <inheritdoc />
    public override string ToString() =>
        Succeeded ? "Succeeded" : $"Failed : {string.Join(",", Errors.Select(e => e.Code))}";
}
