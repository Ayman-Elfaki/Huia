namespace Huia.Eventing;

/// <summary>
/// Published after a user successfully establishes an <c>Identity.Application</c> session —
/// password sign-in, two-factor sign-in, recovery-code sign-in, or passwordless phone/OTP sign-in.
/// <param name="UserId">The signed-in user's id.</param>
/// </summary>
/// <typeparam name="TKey">The identity user's key type (<c>string</c> for Huia's own <c>HuiaUser</c>).</typeparam>
public sealed record UserSignedInEvent<TKey>(TKey UserId);