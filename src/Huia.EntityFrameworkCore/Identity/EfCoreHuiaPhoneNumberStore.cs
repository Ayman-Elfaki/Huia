using Huia.Identity;
using Huia.Stores;
using Microsoft.EntityFrameworkCore;

namespace Huia.EntityFrameworkCore.Identity;

internal sealed class EfCoreHuiaPhoneNumberStore<TContext>(TContext context) : IHuiaPhoneNumberStore
    where TContext : DbContext
{
    public Task<HuiaUser?> FindByNormalizedPhoneNumberAsync(string normalizedPhoneNumber,
        CancellationToken cancellationToken) =>
        context.Set<HuiaUser>()
            .FirstOrDefaultAsync(u => u.NormalizedPhoneNumber == normalizedPhoneNumber, cancellationToken);
}
