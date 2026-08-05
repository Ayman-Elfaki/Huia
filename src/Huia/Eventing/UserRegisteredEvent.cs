namespace Huia.Eventing;

/// <summary>
/// Published after a new user account is created (immediately after <c>UserManager.CreateAsync</c> succeeds).
/// </summary>
public sealed record UserRegisteredEvent(string UserId, string Email);