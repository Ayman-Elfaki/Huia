using Huia.OpenId.Options;
using Huia.Options;

namespace Huia.IntegrationTests;

public class OpenIdOptionsValidationTests
{
    private static HuiaOptions ValidOptions()
    {
        var options = new HuiaOptions { Issuer = new Uri("https://id.example.test") };
        options.AddTenant("acme", tenant =>
        {
            tenant.Authentication.UseEmailAndPasswordLogin();
            tenant.AddHuiaOpenId(openId =>
            {
                openId.AddClient(new HuiaClientDescriptor
                {
                    ClientId = "acme-web",
                    Kind = ClientKind.ServerSideWebApplication,
                    ClientSecret = "s3cret-value",
                    RedirectUris = { new Uri("https://acme.example.test/callback") },
                });
            });
        });
        return options;
    }

    [Fact]
    public void A_confidential_client_without_a_secret_is_rejected()
    {
        var options = ValidOptions();
        options.Tenants["acme"].GetHuiaOpenId()!.Clients[0].ClientSecret = null;

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("confidential client", StringComparison.Ordinal));
    }

    [Fact]
    public void A_public_client_with_a_secret_is_rejected()
    {
        var options = ValidOptions();
        var openId = options.Tenants["acme"].GetHuiaOpenId()!;
        openId.AddClient(new HuiaClientDescriptor
        {
            ClientId = "acme-spa",
            Kind = ClientKind.SinglePageApplication,
            ClientSecret = "should-not-be-here",
            RedirectUris = { new Uri("https://acme.example.test/spa") },
        });

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("public", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_client_ids_within_a_tenant_are_rejected()
    {
        var options = ValidOptions();
        var openId = options.Tenants["acme"].GetHuiaOpenId()!;
        openId.AddClient(new HuiaClientDescriptor
        {
            ClientId = "acme-web",
            Kind = ClientKind.MachineToMachine,
            ClientSecret = "another-secret",
        });

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void External_provider_names_must_be_unique()
    {
        var options = ValidOptions();
        var openId = options.Tenants["acme"].GetHuiaOpenId()!;
        openId.UseExternalLogin(ext =>
        {
            ext.AddOpenIdConnect("Partner", "id-1", "secret-1", "https://partner-a.example.test");
            ext.AddOpenIdConnect("Partner", "id-2", "secret-2", "https://partner-b.example.test");
        });

        Should.Throw<HuiaOptionsException>(() => options.Validate())
            .Errors.ShouldContain(e => e.Contains("duplicated", StringComparison.Ordinal));
    }
}
