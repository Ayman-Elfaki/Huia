namespace Huia.Identity;

/// <summary>Password strength policy. Mirrors ASP.NET Core Identity's <c>PasswordOptions</c>.</summary>
public sealed class HuiaPasswordOptions
{
    /// <summary>Minimum password length. Defaults to 6.</summary>
    public int RequiredLength { get; set; } = 6;

    /// <summary>Minimum number of distinct characters required. Defaults to 1.</summary>
    public int RequiredUniqueChars { get; set; } = 1;

    /// <summary>Whether a digit is required. Defaults to <see langword="true"/>.</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>Whether a lowercase letter is required. Defaults to <see langword="true"/>.</summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>Whether an uppercase letter is required. Defaults to <see langword="true"/>.</summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>Whether a non-alphanumeric character is required. Defaults to <see langword="true"/>.</summary>
    public bool RequireNonAlphanumeric { get; set; } = true;
}
