namespace Huia.Eventing;

/// <summary>
/// Published after a user successfully establishes an <c>Identity.Application</c> session —
/// password sign-in, two-factor sign-in, or recovery-code sign-in.
/// <param name="UserId">The signed-in user's id.</param>
/// <param name="Email">The signed-in user's email.</param>
/// <param name="SessionId">
/// The <c>HuiaUserSession</c> id created for this sign-in — the same value later carried on tokens as the
/// <c>sid</c> claim. <see langword="null"/> only if session creation itself failed in a way the sign-in
/// call site chose not to fail the request over.
/// </param>
/// </summary>
public sealed record UserSignedInEvent(string UserId, string Email, string? SessionId = null);