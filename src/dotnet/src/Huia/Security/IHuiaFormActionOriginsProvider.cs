namespace Huia.Security;

/// <summary>
/// Provides additional <c>form-action</c> origins to be allowed by Content-Security-Policy headers.
/// </summary>
public interface IHuiaFormActionOriginsProvider
{
    /// <summary>Returns the form-action origins.</summary>
    IEnumerable<string> GetOrigins();
}
