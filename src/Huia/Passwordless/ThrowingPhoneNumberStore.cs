using Huia.Identity;
using Huia.Stores;

namespace Huia.Passwordless;

/// <summary>
/// Registered as the fallback <see cref="IHuiaPhoneNumberStore"/> when
/// <c>huia.Authentication.UsePasswordlessFlow()</c> is enabled but no real store has been wired up (via
/// <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c> or <c>.WithStore&lt;TStore,...&gt;()</c>). Throws a
/// clear, actionable error the first time it's actually used, rather than letting the passwordless flow fail
/// with a confusing null-reference or DI-resolution error.
/// </summary>
internal sealed class ThrowingPhoneNumberStore : IHuiaPhoneNumberStore
{
    public Task<HuiaUser?> FindByNormalizedPhoneNumberAsync(string normalizedPhoneNumber,
        CancellationToken cancellationToken) => throw new InvalidOperationException(
        "huia.Authentication.UsePasswordlessFlow() is enabled but no IHuiaPhoneNumberStore is registered. " +
        "Call .WithEntityFrameworkStores<TContext>() (Huia.EntityFrameworkCore) or .WithStore<TStore, ...>() " +
        "on the builder AddHuia returns, or register a custom IHuiaPhoneNumberStore implementation directly.");
}
