namespace Huia.Eventing;

/// <summary>
/// Published after a new user account is created (immediately after <c>UserManager.CreateAsync</c> succeeds).
/// </summary>
/// <typeparam name="TKey">The identity user's key type (<c>string</c> for Huia's own <c>HuiaUser</c>).</typeparam>
public sealed record UserRegisteredEvent<TKey>(TKey UserId, string Email);