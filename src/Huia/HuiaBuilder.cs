using Microsoft.Extensions.DependencyInjection;

namespace Huia;

/// <summary>
/// Returned by <c>services.AddHuia(...)</c> so persistence (<c>WithEntityFrameworkStores</c>) can be
/// chained onto it, mirroring ASP.NET Core Identity's own
/// <see cref="Microsoft.AspNetCore.Identity.IdentityBuilder"/> pattern.
/// </summary>
public sealed class HuiaBuilder(IServiceCollection services)
{
    /// <summary>
    /// The underlying service collection Huia was registered into.
    /// </summary>
    public IServiceCollection Services { get; } = services;
}