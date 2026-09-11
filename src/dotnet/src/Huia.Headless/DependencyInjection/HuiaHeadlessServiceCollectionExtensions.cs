using Finbuckle.MultiTenant.Abstractions;
using Huia;
using Huia.DependencyInjection;
using Huia.Headless;
using Huia.Headless.Options;
using Huia.Headless.Services;
using Huia.Keys;
using Huia.Multitenancy;
using Huia.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering Huia Headless authentication services.</summary>
public static class HuiaHeadlessServiceCollectionExtensions
{
    /// <summary>
    /// Registers Huia Headless bearer token authentication, CORS, token service, and endpoints.
    /// </summary>
    /// <param name="builder">The Huia builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">If combined with Huia.OpenId in the same host.</exception>
    public static IHuiaBuilder AddHuiaHeadless(this IHuiaBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Guard 1: DI-time mutual exclusivity guard with Huia.OpenId
        if (builder.Services.Any(d => d.ServiceType.Name == "HuiaOpenIdMarker" || d.ServiceType.FullName == "Huia.OpenId.HuiaOpenIdMarker"))
        {
            throw new InvalidOperationException(
                "AddHuiaHeadless() cannot be combined with AddHuiaOpenId() in the same host. " +
                "Huia.Headless and Huia.OpenId are independent identity surfaces — pick one.");
        }

        // Guard 3: EF Core DbContext mutual exclusivity guard
        if (builder.Services.Any(d => d.ServiceType.Name.Contains("HuiaOpenIdDbContext")))
        {
            throw new InvalidOperationException(
                "HuiaHeadlessDbContext cannot be combined with HuiaOpenIdDbContext in the same host.");
        }

        builder.Services.TryAddSingleton<HuiaHeadlessMarker>();

        var services = builder.Services;
        var options = builder.Options;

        services.TryAddScoped<IHuiaHeadlessTokenService, HuiaHeadlessTokenService>();

        // CORS configuration
        services.AddCors(cors =>
        {
            cors.AddPolicy("HuiaHeadlessCors", policy =>
            {
                policy.SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrWhiteSpace(origin))
                    {
                        return false;
                    }

                    var cleanOrigin = origin.TrimEnd('/');
                    foreach (var tenant in options.Tenants.Values)
                    {
                        var headless = tenant.GetHuiaHeadless();
                        if (headless is null)
                        {
                            continue;
                        }

                        foreach (var allowed in headless.AllowedOrigins)
                        {
                            if (string.Equals(allowed.TrimEnd('/'), cleanOrigin, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
            });
        });

        // JWT Bearer authentication configuration
        services.AddAuthentication()
            .AddJwtBearer(HuiaHeadlessConstants.Schemes.Bearer, _ => { })
            .AddPolicyScheme(HuiaConstants.Schemes.Api, null, schemeOptions =>
            {
                schemeOptions.ForwardDefault = HuiaHeadlessConstants.Schemes.Bearer;
            });

        services.TryAddSingleton<IPostConfigureOptions<JwtBearerOptions>, HuiaHeadlessJwtPostConfigure>();

        return builder;
    }

    /// <summary>
    /// Registers common Huia and Headless services in a single call.
    /// </summary>
    public static IHuiaBuilder AddHuiaHeadless(this IServiceCollection services, Action<HuiaOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return services.AddHuia(configure).AddHuiaHeadless();
    }

    private sealed class HuiaHeadlessJwtPostConfigure(
        IHttpContextAccessor httpContextAccessor,
        HuiaOptions options) : IPostConfigureOptions<JwtBearerOptions>
    {
        public void PostConfigure(string? name, JwtBearerOptions jwt)
        {
            if (name != HuiaHeadlessConstants.Schemes.Bearer)
            {
                return;
            }

            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerValidator = (issuer, securityToken, validationParameters) =>
                {
                    if (options.Issuer is null)
                    {
                        return issuer;
                    }

                    var baseIssuer = options.Issuer.AbsoluteUri.TrimEnd('/');
                    if (issuer.StartsWith(baseIssuer, StringComparison.OrdinalIgnoreCase))
                    {
                        return issuer;
                    }

                    throw new SecurityTokenInvalidIssuerException($"Invalid issuer: '{issuer}'. Expected base: '{baseIssuer}'.");
                },
                IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                {
                    var httpContext = httpContextAccessor.HttpContext;
                    var keyRing = httpContext?.RequestServices.GetService<IHuiaKeyRing>();
                    var tenantAccessor = httpContext?.RequestServices.GetService<IMultiTenantContextAccessor>();
                    var tenantId = tenantAccessor?.CurrentTenantId();

                    if (tenantId is not null && keyRing is not null)
                    {
                        var keys = keyRing.GetPublishedKeysAsync(tenantId).GetAwaiter().GetResult();
                        if (!string.IsNullOrEmpty(kid))
                        {
                            var match = keys.FirstOrDefault(k => k.KeyId == kid);
                            if (match is not null)
                            {
                                return [match.SecurityKey];
                            }
                        }

                        return keys.Select(k => k.SecurityKey);
                    }

                    return [];
                },
            };
        }
    }
}
