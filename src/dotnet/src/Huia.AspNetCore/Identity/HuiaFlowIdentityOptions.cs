using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Huia.AspNetCore.Identity;

/// <summary>
/// The named <see cref="IdentityOptions"/> instances Huia registers, one per authentication flow.
/// Resolve them with <see cref="IOptionsSnapshot{TOptions}.Get(string)"/>; <see cref="HuiaAuthFlow.Default"/>
/// maps to the unnamed options (<c>Options.DefaultName</c>).
/// </summary>
public static class HuiaFlowIdentityOptions
{
    /// <summary>The named options for <see cref="HuiaAuthFlow.EmailAndPasswordLogin"/>.</summary>
    public const string EmailAndPassword = "huia:flow:password";

    /// <summary>The named options for <see cref="HuiaAuthFlow.PhoneLogin"/>.</summary>
    public const string PhoneLogin = "huia:flow:phone";

    /// <summary>The named options for <see cref="HuiaAuthFlow.ExternalLogin"/>.</summary>
    public const string ExternalLogin = "huia:flow:external";

    /// <summary>The named options for <see cref="HuiaAuthFlow.Passkey"/>.</summary>
    public const string Passkey = "huia:flow:passkey";

    /// <summary>The option name backing a flow.</summary>
    /// <param name="flow">The flow.</param>
    /// <returns>The registered option name; the empty default name for <see cref="HuiaAuthFlow.Default"/>.</returns>
    public static string NameFor(HuiaAuthFlow flow) => flow switch
    {
        HuiaAuthFlow.EmailAndPasswordLogin => EmailAndPassword,
        HuiaAuthFlow.PhoneLogin => PhoneLogin,
        HuiaAuthFlow.ExternalLogin => ExternalLogin,
        HuiaAuthFlow.Passkey => Passkey,
        _ => Microsoft.Extensions.Options.Options.DefaultName,
    };

    /// <summary>The flows that carry their own named options (everything except <see cref="HuiaAuthFlow.Default"/>).</summary>
    public static IReadOnlyList<HuiaAuthFlow> NamedFlows { get; } =
        [HuiaAuthFlow.EmailAndPasswordLogin, HuiaAuthFlow.PhoneLogin, HuiaAuthFlow.ExternalLogin, HuiaAuthFlow.Passkey];
}
