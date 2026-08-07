using Huia.Applications;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Huia.Tests.Unit.Applications;

public class ApplicationDescriptorFactoryTests
{
    [Fact]
    public void BuildPublicInteractiveDescriptor_IsPublicAndRequiresPkce()
    {
        var app = new SinglePageApplicationOptions();
        app.SetClientId("spa");
        app.AddRedirectUri("https://app.example/callback");
        app.AllowScopes("todos");

        var descriptor = ApplicationDescriptorFactory.BuildPublicInteractiveDescriptor(app, ApplicationTypes.Web);

        Assert.Equal(ClientTypes.Public, descriptor.ClientType);
        Assert.Contains(Requirements.Features.ProofKeyForCodeExchange, descriptor.Requirements);
        Assert.Contains(Permissions.GrantTypes.AuthorizationCode, descriptor.Permissions);
        Assert.Contains(Permissions.Prefixes.Scope + "todos", descriptor.Permissions);
        Assert.Contains(Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess, descriptor.Permissions);
        Assert.Contains(new Uri("https://app.example/callback"), descriptor.RedirectUris);
    }

    [Fact]
    public void BuildWebDescriptor_IsConfidentialWithSecret_AndPkceOptional()
    {
        var app = new ServerSideWebApplicationOptions();
        app.SetClientId("web");
        app.SetClientSecret("s3cr3t");
        app.AddRedirectUri("https://web.example/callback");

        var descriptor = ApplicationDescriptorFactory.BuildServerSideWebDescriptor(app);

        Assert.Equal(ClientTypes.Confidential, descriptor.ClientType);
        Assert.Equal("s3cr3t", descriptor.ClientSecret);
        Assert.DoesNotContain(Requirements.Features.ProofKeyForCodeExchange, descriptor.Requirements);
    }

    [Fact]
    public void BuildWebDescriptor_RequiresPkce_WhenRequested()
    {
        var app = new ServerSideWebApplicationOptions();
        app.SetClientId("web");
        app.SetClientSecret("s3cr3t");
        app.RequirePkce();

        var descriptor = ApplicationDescriptorFactory.BuildServerSideWebDescriptor(app);

        Assert.Contains(Requirements.Features.ProofKeyForCodeExchange, descriptor.Requirements);
    }

    [Fact]
    public void BuildMachineToMachineDescriptor_UsesClientCredentials_AndSystematicConsent()
    {
        var app = new MachineToMachineApplicationOptions();
        app.SetClientId("worker");
        app.SetClientSecret("s3cr3t");
        app.AllowScopes("billing");

        var descriptor = ApplicationDescriptorFactory.BuildMachineToMachineDescriptor(app);

        Assert.Equal(ConsentTypes.Systematic, descriptor.ConsentType);
        Assert.Contains(Permissions.GrantTypes.ClientCredentials, descriptor.Permissions);
        Assert.DoesNotContain(Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
            descriptor.Permissions);
        Assert.Contains(Permissions.Prefixes.Scope + "billing", descriptor.Permissions);
    }

    [Fact]
    public void BuildDeviceDescriptor_UsesDeviceCodeGrant()
    {
        var app = new DeviceApplicationOptions();
        app.SetClientId("tv-app");

        var descriptor = ApplicationDescriptorFactory.BuildDeviceDescriptor(app);

        Assert.Equal(ClientTypes.Public, descriptor.ClientType);
        Assert.Contains(Permissions.GrantTypes.DeviceCode, descriptor.Permissions);
        Assert.Contains(Permissions.Endpoints.DeviceAuthorization, descriptor.Permissions);
    }

    [Fact]
    public void AddRedirectUris_GrantsEndSessionPermission_OnlyWhenPostLogoutUrisPresent()
    {
        var withoutLogoutUri = new ServerSideWebApplicationOptions();
        withoutLogoutUri.SetClientId("a");
        withoutLogoutUri.SetClientSecret("s");
        var descriptorWithout =
            ApplicationDescriptorFactory.NewDescriptor(withoutLogoutUri, ApplicationTypes.Web,
                ClientTypes.Confidential);
        ApplicationDescriptorFactory.AddRedirectUris(descriptorWithout, withoutLogoutUri);

        var withLogoutUri = new ServerSideWebApplicationOptions();
        withLogoutUri.SetClientId("b");
        withLogoutUri.SetClientSecret("s");
        withLogoutUri.AddPostLogoutRedirectUri("https://web.example/");
        var descriptorWith =
            ApplicationDescriptorFactory.NewDescriptor(withLogoutUri, ApplicationTypes.Web,
                ClientTypes.Confidential);
        ApplicationDescriptorFactory.AddRedirectUris(descriptorWith, withLogoutUri);

        Assert.DoesNotContain(Permissions.Endpoints.EndSession, descriptorWithout.Permissions);
        Assert.Contains(Permissions.Endpoints.EndSession, descriptorWith.Permissions);
    }

    [Fact]
    public void NewDescriptor_TrustedApp_GetsImplicitConsent()
    {
        var app = new MachineToMachineApplicationOptions();
        app.SetClientId("worker");
        app.SetClientSecret("s");

        var descriptor =
            ApplicationDescriptorFactory.NewDescriptor(app, ApplicationTypes.Web, ClientTypes.Confidential);

        Assert.Equal(ConsentTypes.Implicit, descriptor.ConsentType);
        Assert.Equal("worker", descriptor.DisplayName);
    }

    [Fact]
    public void NewDescriptor_UntrustedApp_RequiresExplicitConsent()
    {
        var app = new MachineToMachineApplicationOptions();
        app.SetClientId("worker");
        app.SetClientSecret("s");
        app.RequireConsent();

        var descriptor =
            ApplicationDescriptorFactory.NewDescriptor(app, ApplicationTypes.Web, ClientTypes.Confidential);

        Assert.Equal(ConsentTypes.Explicit, descriptor.ConsentType);
    }

    [Fact]
    public void NewDescriptor_WithoutLogoutUris_StoresNoLogoutProperties()
    {
        var app = new ServerSideWebApplicationOptions();
        app.SetClientId("web");
        app.SetClientSecret("s");

        var descriptor =
            ApplicationDescriptorFactory.NewDescriptor(app, ApplicationTypes.Web, ClientTypes.Confidential);

        Assert.Empty(descriptor.Properties);
    }

    [Fact]
    public void NewDescriptor_WithoutTokenLifetimesConfigured_StoresNoLifetimeSettings()
    {
        var app = new ServerSideWebApplicationOptions();
        app.SetClientId("web");
        app.SetClientSecret("s");

        var descriptor =
            ApplicationDescriptorFactory.NewDescriptor(app, ApplicationTypes.Web, ClientTypes.Confidential);

        Assert.DoesNotContain(Settings.TokenLifetimes.AccessToken, descriptor.Settings.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain(Settings.TokenLifetimes.IdentityToken, descriptor.Settings.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain(Settings.TokenLifetimes.RefreshToken, descriptor.Settings.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public void NewDescriptor_WithTokenLifetimesConfigured_StoresThemAsSettings()
    {
        var app = new ServerSideWebApplicationOptions();
        app.SetClientId("web");
        app.SetClientSecret("s");
        app.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        app.SetIdentityTokenLifetime(TimeSpan.FromMinutes(20));
        app.SetRefreshTokenLifetime(TimeSpan.FromDays(30));

        var descriptor =
            ApplicationDescriptorFactory.NewDescriptor(app, ApplicationTypes.Web, ClientTypes.Confidential);

        Assert.Equal(TimeSpan.FromMinutes(15).ToString("c"), descriptor.Settings[Settings.TokenLifetimes.AccessToken]);
        Assert.Equal(TimeSpan.FromMinutes(20).ToString("c"),
            descriptor.Settings[Settings.TokenLifetimes.IdentityToken]);
        Assert.Equal(TimeSpan.FromDays(30).ToString("c"), descriptor.Settings[Settings.TokenLifetimes.RefreshToken]);
    }
}