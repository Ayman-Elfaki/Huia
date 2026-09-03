namespace Huia.AspNetCore;

/// <summary>
/// Marker type for the shared localization resources. It lives in the assembly root namespace with its
/// <c>.resx</c> files under <c>Resources/</c>; the SDK's <c>DependentUpon</c> convention then names the
/// resource <c>Huia.AspNetCore.SharedResource.resources</c> (no <c>.Resources.</c> segment), so
/// <c>LocalizationOptions.ResourcesPath</c> must be left empty rather than set to <c>"Resources"</c>.
/// </summary>
public sealed class SharedResource
{
}
