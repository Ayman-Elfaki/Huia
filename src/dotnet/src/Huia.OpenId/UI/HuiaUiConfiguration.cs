using System.Text.Encodings.Web;
using System.Text.Unicode;
using Huia.DependencyInjection;
using Huia.Emails;
using Huia.OpenId.Flows;
using Huia.OpenId.UI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.WebEncoders;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Opt-in for the Razor Pages account UI (login, passwordless, profile completion, emails).</summary>
public static class HuiaUiConfiguration
{
    /// <summary>
    /// Adds the account UI: the Razor Pages class library, view localization, the flow-token protector and
    /// the cookie login paths. Without this, <c>AddHuiaOpenId</c> still serves the protocol endpoints but has no
    /// interactive sign-in surface.
    /// </summary>
    /// <param name="builder">The Huia builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHuiaBuilder AddHuiaUi(this IHuiaBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.TryAddSingleton<HuiaUiMarker>();
        services.TryAddScoped<IReturnUrlProtector, ReturnUrlProtector>();
        services.TryAddScoped<RazorEmailRenderer>();
        services.TryAddScoped<IHuiaEmailSender, HuiaEmailSender>();

        services.AddRazorPages()
            .AddApplicationPart(typeof(HuiaUiMarker).Assembly)
            .AddApplicationPart(typeof(RazorEmailRenderer).Assembly)
            .AddViewLocalization()
            .AddDataAnnotationsLocalization();

        // Render literal UTF-8 (Arabic strings, email bodies) instead of \uXXXX escapes.
        services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            options.LoginPath = "/identity/account/login";
            options.LogoutPath = "/identity/account/logout";
            options.AccessDeniedPath = "/identity/account/accessdenied";
            options.ReturnUrlParameter = "returnUrl";
        });

        return builder;
    }
}
