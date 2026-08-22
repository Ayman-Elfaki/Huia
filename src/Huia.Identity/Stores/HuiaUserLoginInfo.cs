namespace Huia.Stores;

/// <summary>An external (third-party) login linked to a <see cref="Huia.Identity.HuiaUser"/>. Replaces
/// ASP.NET Core Identity's <c>UserLoginInfo</c>.</summary>
/// <param name="LoginProvider">The provider's logical name (e.g. <c>"Google"</c>).</param>
/// <param name="ProviderKey">The user's unique id at the provider (the OIDC <c>sub</c> claim).</param>
/// <param name="ProviderDisplayName">The provider's display name, for UI.</param>
public sealed record HuiaUserLoginInfo(string LoginProvider, string ProviderKey, string? ProviderDisplayName);
