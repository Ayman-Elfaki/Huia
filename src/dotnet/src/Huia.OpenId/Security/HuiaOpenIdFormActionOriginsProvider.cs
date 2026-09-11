using Huia.OpenId.Options;
using Huia.Options;
using Huia.Security;

namespace Huia.OpenId.Security;

internal sealed class HuiaOpenIdFormActionOriginsProvider(HuiaOptions huiaOptions) : IHuiaFormActionOriginsProvider
{
    public IEnumerable<string> GetOrigins()
    {
        return huiaOptions.Tenants.Values
            .SelectMany(tenant =>
            {
                var openId = tenant.GetHuiaOpenId();
                if (openId is null)
                {
                    return [];
                }

                var clientUris = openId.Clients
                    .SelectMany(client => client.RedirectUris.Concat(client.PostLogoutRedirectUris).Concat(client.HomeUris));

                var providerUris = openId.External?.Providers
                    .Where(provider => provider.Authority is not null)
                    .Select(provider => new Uri(provider.Authority!, UriKind.Absolute)) ?? [];

                return clientUris.Concat(providerUris);
            })
            .Where(uri => uri.IsAbsoluteUri)
            .Select(uri => uri.GetLeftPart(UriPartial.Authority))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
