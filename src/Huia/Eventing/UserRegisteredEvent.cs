namespace Huia.Eventing;

/// <summary>
/// Published after a new user account is created (immediately after <c>UserManager.CreateAsync</c> succeeds).
/// <c>Email</c> is <see langword="null"/> for a phone-only (passwordless) account with no email on file.
/// </summary>
/// <typeparam name="TKey">The identity user's key type (<c>string</c> for Huia's own <c>HuiaUser</c>).</typeparam>
public sealed record UserRegisteredEvent<TKey>(TKey UserId, string? Email);