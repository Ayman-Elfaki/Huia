using Huia.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Huia.Keys;

/// <summary>Registers a custom (non-EF-Core) <see cref="ISigningKeyStore"/> implementation.</summary>
public static class StoreBuilderExtensions
{
    /// <summary>
    /// Backs Huia's key management (automatic or manual) with a custom <typeparamref name="TKeyStore"/>
    /// implementation of <see cref="ISigningKeyStore"/> — independent of however you're storing
    /// everything else (<c>WithEntityFrameworkStores</c> or core <c>Huia</c>'s <c>WithStore</c>), since
    /// signing-key storage is its own extension point. <typeparamref name="TKeyStore"/> can be the same type
    /// you pass to <c>WithStore&lt;TStore, ...&gt;()</c> (if it also implements <see cref="ISigningKeyStore"/>)
    /// or a dedicated type entirely (e.g. backing keys with a cloud KMS while everything else stays on EF
    /// Core). Order relative to <c>huia.KeysManagement.UseAutomaticKeyManagement()</c>/
    /// <c>UseManualKeyManagement()</c> (configured inside <c>AddHuia</c>) doesn't matter.
    /// </summary>
    /// <param name="builder">The builder returned by <c>services.AddHuia(...)</c>.</param>
    /// <param name="configure">
    /// Configures <see cref="KeyManagementOptions"/> (e.g. <see cref="KeyManagementOptions.RsaKeySizeInBits"/>),
    /// needed by <typeparamref name="TKeyStore"/> regardless of which key management mode is enabled.
    /// </param>
    public static HuiaBuilder WithSigningKeyStore<TKeyStore>(
        this HuiaBuilder builder,
        Action<KeyManagementOptions>? configure = null)
        where TKeyStore : class, ISigningKeyStore
    {
        var services = builder.Services;

        // TryAddSingleton leaves an already-configured instance from a prior WithSigningKeyStore/
        // WithEntityFrameworkStores/KeysManagement.UseAutomaticKeyManagement call alone, so configuration
        // composes regardless of call order.
        var options = services.FirstOrDefault(d => d.ServiceType == typeof(KeyManagementOptions))
                ?.ImplementationInstance
            as KeyManagementOptions ?? new KeyManagementOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.AddScoped<ISigningKeyStore, TKeyStore>();

        return builder;
    }
}