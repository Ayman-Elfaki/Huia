namespace Huia.Applications;

/// <summary>
/// The well-known key <see cref="ApplicationDescriptorFactory"/> stashes
/// <see cref="ClientApplicationOptions.HomeUri"/> under inside <c>OpenIddictApplicationDescriptor.Properties</c>
/// — OpenIddict has no first-class concept of it, but persists arbitrary JSON under <c>Properties</c> for
/// exactly this kind of extension data. Shared with <c>Huia.Helpers.ClientHomeResolver</c>, which reads it
/// back.
/// </summary>
internal static class HomeUriApplicationProperty
{
    public const string HomeUri = "huia:home_uri";
}
