using Huia.Identity;
using Huia.Keys;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Huia.Stores;

/// <summary>
/// Backs Huia's core persistence — ASP.NET Core Identity's user/role storage, OpenIddict's
/// application/authorization/scope/token storage, and signing/encryption key storage — with a single
/// implementation. This is the extension point for a fully custom, non-EF-Core backend (e.g. Dapper);
/// register an implementation with <c>.WithStore&lt;TStore, ...&gt;()</c> instead of calling
/// <c>.WithEntityFrameworkStores&lt;TContext&gt;()</c>.
/// </summary>
/// <remarks>
/// Composes existing, already provider-agnostic store interfaces from ASP.NET Core Identity and
/// OpenIddict, plus <see cref="Huia.Keys.ISigningKeyStore"/> for key management (automatic or manual) —
/// needed only if you enable <c>huia.KeysManagement.UseAutomaticKeyManagement()</c>/
/// <c>UseManualKeyManagement()</c>, but implemented regardless since it's part of this interface.
/// <c>WithStore&lt;TStore, ...&gt;()</c> registers <c>TStore</c> as
/// <see cref="Huia.Keys.ISigningKeyStore"/> too. <see cref="IUserLoginStore{TUser}"/> backs external
/// (third-party) sign-in providers registered via <c>huia.ExternalLogins</c> — needed only if you register
/// any, but implemented regardless since it's part of this interface. See docs/custom-store.md,
/// docs/key-management.md, and docs/external-providers.md.
/// </remarks>
public interface IHuiaStore<TApplication, TAuthorization, TScope, TToken> :
    IUserStore<HuiaUser>,
    IUserLoginStore<HuiaUser>,
    IRoleStore<HuiaRole>,
    IOpenIddictApplicationStore<TApplication>,
    IOpenIddictAuthorizationStore<TAuthorization>,
    IOpenIddictScopeStore<TScope>,
    IOpenIddictTokenStore<TToken>,
    ISigningKeyStore
    where TApplication : class
    where TAuthorization : class
    where TScope : class
    where TToken : class;