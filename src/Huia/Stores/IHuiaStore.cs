using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Huia.Stores;

/// <summary>
/// Backs Huia's core persistence — ASP.NET Core Identity's user/role storage and OpenIddict's
/// application/authorization/scope/token storage — with a single implementation. This is the extension
/// point for a fully custom, non-EF-Core backend (e.g. Dapper); register an implementation with
/// <c>.WithStore&lt;TStore, ...&gt;()</c> instead of calling <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c>.
/// </summary>
/// <remarks>
/// This interface adds no members of its own — it composes existing, already provider-agnostic store
/// interfaces from ASP.NET Core Identity and OpenIddict. It says nothing about signing-key storage: that's
/// <see cref="Huia.Keys.ISigningKeyStore"/>, a separate, independent extension point only
/// needed if you also want automatic or manual key management — register an implementation alongside
/// <c>WithStore</c> via <c>.WithSigningKeyStore&lt;TKeyStore&gt;()</c> if you want that too. See
/// docs/custom-store.md and docs/key-management.md.
/// </remarks>
public interface IHuiaStore<TApplication, TAuthorization, TScope, TToken> :
    IUserStore<HuiaUser>,
    IRoleStore<HuiaRole>,
    IOpenIddictApplicationStore<TApplication>,
    IOpenIddictAuthorizationStore<TAuthorization>,
    IOpenIddictScopeStore<TScope>,
    IOpenIddictTokenStore<TToken>
    where TApplication : class
    where TAuthorization : class
    where TScope : class
    where TToken : class;