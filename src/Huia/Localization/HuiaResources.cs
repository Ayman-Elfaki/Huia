namespace Huia.Localization;

/// <summary>
/// Marker type associating Huia.UI's shared string resources (<c>HuiaResources.resx</c> for English,
/// <c>HuiaResources.ar.resx</c> for Arabic) with <c>IStringLocalizer&lt;HuiaResources&gt;</c>. Injected
/// automatically into every Razor page under <c>Areas/Identity/Pages</c> (see <c>_ViewImports.cshtml</c>)
/// and into <see cref="Huia.Emails.HuiaEmailTemplate"/>.
/// </summary>
public sealed class HuiaResources;