using Huia.Identity;
using Huia.Stores;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Identity;

/// <summary>
/// EF Core-backed <see cref="IHuiaUserStore"/>/<see cref="IHuiaUserLoginStore"/>/<see cref="IHuiaUserRoleStore"/>/
/// <see cref="IHuiaUserTokenStore"/>/<see cref="IHuiaPhoneNumberStore"/>, registered by
/// <c>WithEntityFrameworkStores&lt;TContext&gt;()</c>.
/// </summary>
internal sealed class EfCoreHuiaUserStore<TContext>(TContext context) :
    IHuiaUserStore, IHuiaUserLoginStore, IHuiaUserRoleStore, IHuiaUserTokenStore, IHuiaPhoneNumberStore
    where TContext : DbContext
{
    #region IHuiaUserStore

    public Task<HuiaUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        context.Set<HuiaUser>().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<HuiaUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        context.Set<HuiaUser>().FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);

    public Task<HuiaUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Set<HuiaUser>().FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public async Task CreateAsync(HuiaUser user, CancellationToken cancellationToken)
    {
        context.Set<HuiaUser>().Add(user);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(HuiaUser user, CancellationToken cancellationToken)
    {
        // Not Update(user) — if `user` was loaded earlier in this same (scoped) context, it's already
        // tracked, and re-attaching it would throw. If it's genuinely detached, EF attaches it as Modified
        // the first time a property is touched via the entry below anyway.
        context.Entry(user).State = context.Entry(user).State == EntityState.Detached
            ? EntityState.Modified
            : context.Entry(user).State;

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(HuiaUser user, CancellationToken cancellationToken)
    {
        context.Set<HuiaUser>().Remove(user);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public IQueryable<HuiaUser>? Users => context.Set<HuiaUser>();

    #endregion

    #region IHuiaUserLoginStore

    public async Task AddLoginAsync(HuiaUser user, HuiaUserLoginInfo login, CancellationToken cancellationToken)
    {
        context.Set<HuiaUserLogin>().Add(new HuiaUserLogin
        {
            LoginProvider = login.LoginProvider,
            ProviderKey = login.ProviderKey,
            ProviderDisplayName = login.ProviderDisplayName,
            UserId = user.Id,
        });

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveLoginAsync(HuiaUser user, string loginProvider, string providerKey,
        CancellationToken cancellationToken)
    {
        var entity = await context.Set<HuiaUserLogin>()
            .FirstOrDefaultAsync(l => l.UserId == user.Id && l.LoginProvider == loginProvider &&
                                      l.ProviderKey == providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return;
        }

        context.Set<HuiaUserLogin>().Remove(entity);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HuiaUserLoginInfo>> GetLoginsAsync(HuiaUser user, CancellationToken cancellationToken) =>
        await context.Set<HuiaUserLogin>()
            .Where(l => l.UserId == user.Id)
            .Select(l => new HuiaUserLoginInfo(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<HuiaUser?> FindByLoginAsync(string loginProvider, string providerKey,
        CancellationToken cancellationToken)
    {
        var login = await context.Set<HuiaUserLogin>()
            .FirstOrDefaultAsync(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey,
                cancellationToken)
            .ConfigureAwait(false);

        return login is null ? null : await FindByIdAsync(login.UserId, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region IHuiaUserRoleStore

    public async Task AddToRoleAsync(HuiaUser user, string roleName, CancellationToken cancellationToken)
    {
        var role = await FindRoleByNameAsync(roleName, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

        context.Set<HuiaUserRole>().Add(new HuiaUserRole { UserId = user.Id, RoleId = role.Id });
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveFromRoleAsync(HuiaUser user, string roleName, CancellationToken cancellationToken)
    {
        var role = await FindRoleByNameAsync(roleName, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return;
        }

        var entity = await context.Set<HuiaUserRole>()
            .FirstOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        context.Set<HuiaUserRole>().Remove(entity);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(HuiaUser user, CancellationToken cancellationToken) =>
        await (from userRole in context.Set<HuiaUserRole>()
               join role in context.Set<HuiaRole>() on userRole.RoleId equals role.Id
               where userRole.UserId == user.Id
               select role.Name!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> IsInRoleAsync(HuiaUser user, string roleName, CancellationToken cancellationToken)
    {
        var role = await FindRoleByNameAsync(roleName, cancellationToken).ConfigureAwait(false);
        return role is not null && await context.Set<HuiaUserRole>()
            .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HuiaUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var role = await FindRoleByNameAsync(roleName, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return [];
        }

        return await (from userRole in context.Set<HuiaUserRole>()
                       join user in context.Set<HuiaUser>() on userRole.UserId equals user.Id
                       where userRole.RoleId == role.Id
                       select user)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<HuiaRole?> FindRoleByNameAsync(string roleName, CancellationToken cancellationToken)
    {
        var normalized = roleName.Trim().ToUpperInvariant();
        return context.Set<HuiaRole>().FirstOrDefaultAsync(r => r.NormalizedName == normalized, cancellationToken);
    }

    #endregion

    #region IHuiaUserTokenStore

    public async Task SetTokenAsync(HuiaUser user, string loginProvider, string name, string? value,
        CancellationToken cancellationToken)
    {
        var entity = await context.Set<HuiaUserToken>()
            .FirstOrDefaultAsync(t => t.UserId == user.Id && t.Provider == loginProvider && t.Name == name,
                cancellationToken)
            .ConfigureAwait(false);

        if (value is null)
        {
            if (entity is not null)
            {
                context.Set<HuiaUserToken>().Remove(entity);
                await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (entity is null)
        {
            context.Set<HuiaUserToken>().Add(new HuiaUserToken
            {
                UserId = user.Id,
                Provider = loginProvider,
                Name = name,
                Value = value,
            });
        }
        else
        {
            entity.Value = value;
        }

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetTokenAsync(HuiaUser user, string loginProvider, string name,
        CancellationToken cancellationToken)
    {
        var entity = await context.Set<HuiaUserToken>()
            .FirstOrDefaultAsync(t => t.UserId == user.Id && t.Provider == loginProvider && t.Name == name,
                cancellationToken)
            .ConfigureAwait(false);

        return entity?.Value;
    }

    #endregion

    #region IHuiaPhoneNumberStore

    public Task<HuiaUser?> FindByNormalizedPhoneNumberAsync(string normalizedPhoneNumber,
        CancellationToken cancellationToken) =>
        context.Set<HuiaUser>().FirstOrDefaultAsync(u => u.NormalizedPhoneNumber == normalizedPhoneNumber,
            cancellationToken);

    #endregion

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
