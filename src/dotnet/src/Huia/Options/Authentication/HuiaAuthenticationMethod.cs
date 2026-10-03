namespace Huia.Options;

/// <summary>
/// Abstract base class for an authentication method enabled for a tenant.
/// </summary>
public abstract class HuiaAuthenticationMethod : IHuiaOptionsSection
{
    /// <summary>The unique method identifier or name (e.g. EmailPassword, Passkey, Phone, External).</summary>
    public abstract string MethodType { get; }

    /// <summary>Whether this authentication method is enabled.</summary>
    public virtual bool IsEnabled { get; set; } = true;

    /// <summary>Validates the method configuration.</summary>
    public virtual void Validate(string path, List<string> errors)
    {
    }

    void IHuiaOptionsSection.Validate(string path, List<string> errors) => Validate(path, errors);
}
