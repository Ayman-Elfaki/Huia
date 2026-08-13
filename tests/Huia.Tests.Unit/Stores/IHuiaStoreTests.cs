using Huia.Identity;
using Huia.Keys;
using Huia.Stores;
using Microsoft.AspNetCore.Identity;

namespace Huia.Tests.Unit.Stores;

public class IHuiaStoreTests
{
    /// <summary>
    /// The behavior the ISigningKeyStore/IHuiaStore merge actually changes: a type implementing
    /// IHuiaStore now implements ISigningKeyStore too, so WithStore&lt;TStore, ...&gt;() (see
    /// StoreBuilderExtensions.cs) can register TStore as ISigningKeyStore alongside the Identity/OpenIddict
    /// store registrations. A full fake IHuiaStore implementation would additionally need every OpenIddict
    /// application/authorization/scope/token store member — this asserts the composition directly instead.
    /// </summary>
    [Fact]
    public void IHuiaStore_ComposesISigningKeyStore()
    {
        var interfaces = typeof(IHuiaStore<,,,>).GetInterfaces();

        Assert.Contains(typeof(ISigningKeyStore), interfaces);
    }

    /// <summary>
    /// A type implementing IHuiaStore also implements IUserLoginStore&lt;HuiaUser&gt; — the interface backing
    /// external (third-party) sign-in providers. WithStore&lt;TStore, ...&gt;() registers TStore as
    /// IUserStore&lt;HuiaUser&gt; only; UserManager&lt;HuiaUser&gt; resolves IUserLoginStore&lt;HuiaUser&gt; by
    /// casting that same injected instance, so no separate registration is needed as long as this
    /// composition holds.
    /// </summary>
    [Fact]
    public void IHuiaStore_ComposesIUserLoginStore()
    {
        var interfaces = typeof(IHuiaStore<,,,>).GetInterfaces();

        Assert.Contains(typeof(IUserLoginStore<HuiaUser>), interfaces);
    }
}
