using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Identity;

/// <summary>
/// ASP.NET Core Identity's built-in <see cref="UserStore{TUser,TRole,TContext}"/>, specialized for
/// <see cref="HuiaUser"/>/<see cref="HuiaRole"/>, with <see cref="DeleteAsync"/> overridden to replace the
/// database-level <c>ON DELETE CASCADE</c> that Huia's table-per-concrete-type (TPC) user hierarchy can't
/// have — see <c>ModelBuilderExtensions.UseHuiaTpcUserHierarchy</c> for why the foreign keys from
/// <c>HuiaUserClaims</c>/<c>HuiaUserLogins</c>/<c>HuiaUserTokens</c>/<c>HuiaUserRoles</c> into
/// <see cref="HuiaUser"/> are removed from the model. Every other member is inherited unchanged from
/// <see cref="UserStore{TUser,TRole,TContext}"/>.
/// </summary>
internal sealed class EfCoreHuiaUserStore<TContext>(TContext context, IdentityErrorDescriber? describer = null)
    : UserStore<HuiaUser, HuiaRole, TContext>(context, describer)
    where TContext : DbContext
{
    public override async Task<IdentityResult> DeleteAsync(HuiaUser user, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);

        var transaction = await Context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Context.RemoveRange(await Context.Set<IdentityUserClaim<string>>()
                .Where(c => c.UserId == user.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
            Context.RemoveRange(await Context.Set<IdentityUserLogin<string>>()
                .Where(l => l.UserId == user.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
            Context.RemoveRange(await Context.Set<IdentityUserToken<string>>()
                .Where(t => t.UserId == user.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
            Context.RemoveRange(await Context.Set<IdentityUserRole<string>>()
                .Where(r => r.UserId == user.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
            await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var result = await base.DeleteAsync(user, cancellationToken).ConfigureAwait(false);

            if (result.Succeeded)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}
