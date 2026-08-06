using Huia.Keys;
using Huia.Stores;

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
}
