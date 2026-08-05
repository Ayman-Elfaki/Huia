using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;

namespace Huia.Scopes;

/// <summary>
/// Idempotently upserts the scopes registered via <see cref="ScopesBuilder"/> into the OpenIddict scope
/// store on startup — the same store the <c>/admin/scopes</c> endpoints manage at runtime.
/// </summary>
internal sealed class ScopeInitializer(IServiceScopeFactory scopeFactory, ScopesBuilder scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        foreach (var declared in scopes.Scopes)
        {
            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = declared.Name,
                DisplayName = declared.DisplayName,
                Description = declared.Description,
            };

            foreach (var resource in declared.Resources)
            {
                descriptor.Resources.Add(resource);
            }

            var existing = await manager.FindByNameAsync(declared.Name, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                await manager.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await manager.UpdateAsync(existing, descriptor, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}