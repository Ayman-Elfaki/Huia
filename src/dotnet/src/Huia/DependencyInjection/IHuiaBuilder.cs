using Huia.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.DependencyInjection;

/// <summary>
/// The result of <c>AddHuiaOpenId(...)</c> or <c>AddHuiaHeadless(...)</c>. Further opt-ins — EF Core store
/// registration (<c>AddEntityFrameworkCoreStores()</c>), the account UI, security headers, … — hang off
/// this builder as extension methods so <c>Program.cs</c> reads as one fluent chain.
/// </summary>
public interface IHuiaBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>The validated options the identity provider was configured with.</summary>
    HuiaOptions Options { get; }
}

/// <summary>Default <see cref="IHuiaBuilder"/> implementation.</summary>
internal sealed class HuiaBuilder(IServiceCollection services, HuiaOptions options) : IHuiaBuilder
{
    public IServiceCollection Services { get; } = services;

    public HuiaOptions Options { get; } = options;
}
