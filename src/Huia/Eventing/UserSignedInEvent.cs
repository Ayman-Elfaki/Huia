namespace Huia.Eventing;

/// <summary>
/// Published after a user successfully establishes an <c>Identity.Application</c> session —
/// password sign-in, two-factor sign-in, or recovery-code sign-in.
/// <param name="UserId">The signed-in user's id.</param>
/// <param name="Email">The signed-in user's email.</param>
/// </summary>
public sealed record UserSignedInEvent(string UserId, string Email);