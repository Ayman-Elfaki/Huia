namespace Huia.Sessions;

/// <summary>
/// The well-known key under which <see cref="AttachSessionAuthorizationHandler"/> stashes a session's id
/// inside an <c>OpenIddictAuthorizationDescriptor.Properties</c> bag — OpenIddict has no first-class concept
/// of "which session created this authorization," but persists arbitrary JSON under <c>Properties</c> for
/// exactly this kind of extension data (the same technique
/// <c>Huia.Applications.LogoutApplicationProperties</c> already uses for front/back-channel logout URIs).
/// Read back by <see cref="SessionAuthorizationLookup"/>.
/// </summary>
internal static class SessionAuthorizationProperties
{
    public const string SessionId = "huia:session_id";
}
