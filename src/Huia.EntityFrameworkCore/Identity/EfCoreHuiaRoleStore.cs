using Huia.Identity;
using Huia.Stores;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Identity;

/// <summary>EF Core-backed <see cref="IHuiaRoleStore"/>, registered by <c>WithEntityFrameworkStores&lt;TContext&gt;()</c>.</summary>
internal sealed class EfCoreHuiaRoleStore<TContext>(TContext context) : IHuiaRoleStore
    where TContext : DbContext
{
    public Task<HuiaRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
        context.Set<HuiaRole>().FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

    public Task<HuiaRole?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken) =>
        context.Set<HuiaRole>().FirstOrDefaultAsync(r => r.NormalizedName == normalizedName, cancellationToken);

    public async Task CreateAsync(HuiaRole role, CancellationToken cancellationToken)
    {
        context.Set<HuiaRole>().Add(role);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(HuiaRole role, CancellationToken cancellationToken)
    {
        var entry = context.Entry(role);
        entry.State = entry.State == EntityState.Detached ? EntityState.Modified : entry.State;

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(HuiaRole role, CancellationToken cancellationToken)
    {
        context.Set<HuiaRole>().Remove(role);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public IQueryable<HuiaRole>? Roles => context.Set<HuiaRole>();

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new HuiaConcurrencyException("The entity was modified concurrently.", ex);
        }
    }
}
