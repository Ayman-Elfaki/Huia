using Huia.AspNetCore.Keys.Jobs;
using Huia.AspNetCore.OpenIddict.Handlers;
using Huia.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Huia.AspNetCore.Keys;

/// <summary>Registers the signing-key services, the start-up bootstrapper and the Quartz lifecycle jobs.</summary>
internal static class HuiaKeyManagementConfiguration
{
    public static IServiceCollection AddHuiaKeyManagement(this IServiceCollection services, HuiaOptions options)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IHuiaKeyProtector, DataProtectionKeyProtector>();
        services.AddSingleton<HuiaSigningKeyFactory>();
        services.AddSingleton<IHuiaKeyRing, HuiaKeyRing>();
        services.AddScoped<HuiaKeyLifecycleService>();
        services.AddHostedService<HuiaKeyBootstrapper>();

        // Scoped OpenIddict custom handlers (also referenced by their static descriptors).
        services.AddScoped<HuiaTenantSigningKeyHandler>();
        services.AddScoped<HuiaTenantTokenValidationHandler>();
        services.AddScoped<HuiaTenantServerTokenValidationHandler>();
        services.AddScoped<HuiaTenantJwksHandler>();

        if (!options.Keys.EnableBackgroundJobs)
        {
            return services;
        }

        services.AddQuartz(quartz =>
        {
            Schedule<HuiaKeyActivationJob>(quartz, "huia-key-activation", "0 0/5 * * * ?");
            Schedule<HuiaKeyRotationJob>(quartz, "huia-key-rotation", "0 7 * * * ?");
            Schedule<HuiaKeyRetirementJob>(quartz, "huia-key-retirement", "0 23 * * * ?");
            Schedule<HuiaKeyDeletionJob>(quartz, "huia-key-deletion", "0 41 3 * * ?");
        });

        return services;
    }

    private static void Schedule<TJob>(IServiceCollectionQuartzConfigurator quartz, string name, string cron)
        where TJob : IJob
    {
        var key = new JobKey(name, "huia-keys");
        quartz.AddJob<TJob>(job => job.WithIdentity(key));
        quartz.AddTrigger(trigger => trigger
            .ForJob(key)
            .WithIdentity($"{name}-trigger", "huia-keys")
            .WithCronSchedule(cron));
    }
}
