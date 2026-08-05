namespace Huia.Applications;

/// <summary>
/// Well-known keys under which <see cref="ApplicationDescriptorFactory"/> stashes
/// <see cref="ClientApplicationOptions.FrontChannelLogoutUri"/>/<see cref="ClientApplicationOptions.BackChannelLogoutUri"/>
/// inside <c>OpenIddictApplicationDescriptor.Properties</c> — OpenIddict has no first-class concept of
/// either URI, but persists arbitrary JSON under <c>Properties</c> for exactly this kind of extension data.
/// Shared with <c>Huia.Endpoints.Connect.LogoutNotifier</c>, which reads them back at logout time.
/// </summary>
internal static class LogoutApplicationProperties
{
    public const string FrontChannelLogoutUri = "huia:frontchannel_logout_uri";
    public const string BackChannelLogoutUri = "huia:backchannel_logout_uri";
}



/// <summary>
/// The well-known key <see cref="ApplicationDescriptorFactory"/> stashes
/// <see cref="ClientApplicationOptions.HomeUri"/> under inside <c>OpenIddictApplicationDescriptor.Properties</c>
/// — OpenIddict has no first-class concept of it, but persists arbitrary JSON under <c>Properties</c> for
/// exactly this kind of extension data. Shared with <c>Huia.Helpers.ClientHomeResolver</c>, which reads it
/// back. Same pattern as <see cref="LogoutApplicationProperties"/>.
/// </summary>
internal static class HomeUriApplicationProperty
{
    public const string HomeUri = "huia:home_uri";
}
