using Huia.Headless.Options;

namespace Huia.Options;

/// <summary>Extension methods for attaching Headless tenant options to <see cref="TenantOptions"/>.</summary>
public static class HuiaHeadlessTenantOptionsExtensions
{
    /// <summary>Configures Huia Headless options for this tenant.</summary>
    /// <param name="tenant">The tenant options.</param>
    /// <param name="configure">Configuration callback.</param>
    /// <returns>The same tenant options, for chaining.</returns>
    /// <exception cref="HuiaOptionsException">If the tenant already has OpenID options configured.</exception>
    public static TenantOptions AddHuiaHeadless(this TenantOptions tenant, Action<HuiaHeadlessTenantOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(configure);

        foreach (var extensionType in tenant.Extensions.Keys)
        {
            if (extensionType.Name.Contains("OpenId", StringComparison.OrdinalIgnoreCase))
            {
                throw new HuiaOptionsException(
                    "AddHuiaHeadless() cannot be configured on a tenant that already has OpenId options configured. " +
                    "Huia.Headless and Huia.OpenId are mutually exclusive.");
            }
        }

        var headlessOptions = tenant.GetOrAddExtension(() => new HuiaHeadlessTenantOptions());
        configure(headlessOptions);
        return tenant;
    }

    /// <summary>Gets the configured Headless options for this tenant, or null if none.</summary>
    public static HuiaHeadlessTenantOptions? GetHuiaHeadless(this TenantOptions tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return tenant.Extensions.TryGetValue(typeof(HuiaHeadlessTenantOptions), out var ext)
            ? (HuiaHeadlessTenantOptions)ext
            : null;
    }
}
