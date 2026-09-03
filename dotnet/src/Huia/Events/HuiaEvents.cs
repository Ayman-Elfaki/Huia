namespace Huia.Events;

/// <summary>Raised after a new user account has been persisted.</summary>
/// <param name="TenantId">The tenant the user belongs to.</param>
/// <param name="UserId">The new user's identifier.</param>
/// <param name="UserName">The new user's user name.</param>
/// <param name="Email">The new user's email address, if one was supplied.</param>
/// <param name="Method">How the account was created: <c>password</c>, <c>sms</c> or an external provider name.</param>
/// <param name="OccurredAt">When the account was created (UTC).</param>
public sealed record UserRegisteredEvent(
    string TenantId,
    string UserId,
    string UserName,
    string? Email,
    string Method,
    DateTimeOffset OccurredAt) : IHuiaEvent;

/// <summary>Raised after a user has successfully signed in (interactive or token flow).</summary>
/// <param name="TenantId">The tenant the user belongs to.</param>
/// <param name="UserId">The user's identifier.</param>
/// <param name="Method">The authentication method: <c>password</c>, <c>sms</c> or an external provider name.</param>
/// <param name="ClientId">The OAuth client the sign-in was performed for, if any.</param>
/// <param name="OccurredAt">When the sign-in occurred (UTC).</param>
public sealed record UserLoggedInEvent(
    string TenantId,
    string UserId,
    string Method,
    string? ClientId,
    DateTimeOffset OccurredAt) : IHuiaEvent;

/// <summary>Raised after a user's password has been set or changed.</summary>
/// <param name="TenantId">The tenant the user belongs to.</param>
/// <param name="UserId">The user's identifier.</param>
/// <param name="Reset">
/// <see langword="true"/> when the change came from a reset-password flow, <see langword="false"/> for an
/// authenticated change.
/// </param>
/// <param name="OccurredAt">When the change occurred (UTC).</param>
public sealed record PasswordChangedEvent(
    string TenantId,
    string UserId,
    bool Reset,
    DateTimeOffset OccurredAt) : IHuiaEvent;

/// <summary>Raised whenever a one-time code has been generated and a delivery attempt made.</summary>
/// <param name="TenantId">The tenant the request was made in.</param>
/// <param name="UserId">
/// The user the code was issued for, or <see langword="null"/> when the number has no account yet
/// (deferred auto-provisioning).
/// </param>
/// <param name="PhoneNumberMask">The destination number with only the last four digits retained.</param>
/// <param name="Delivered"><see langword="true"/> when the SMS provider accepted the message.</param>
/// <param name="OccurredAt">When the code was requested (UTC).</param>
public sealed record OtpRequestedEvent(
    string TenantId,
    string? UserId,
    string PhoneNumberMask,
    bool Delivered,
    DateTimeOffset OccurredAt) : IHuiaEvent;

/// <summary>Raised after a one-time code has been verified successfully.</summary>
/// <param name="TenantId">The tenant the verification was made in.</param>
/// <param name="UserId">The user the code belonged to, if the account already existed.</param>
/// <param name="PhoneNumberMask">The verified number with only the last four digits retained.</param>
/// <param name="OccurredAt">When the verification occurred (UTC).</param>
public sealed record OtpVerifiedEvent(
    string TenantId,
    string? UserId,
    string PhoneNumberMask,
    DateTimeOffset OccurredAt) : IHuiaEvent;

/// <summary>Raised when a user's phone number is confirmed or removed.</summary>
/// <param name="TenantId">The tenant the user belongs to.</param>
/// <param name="UserId">The user's identifier.</param>
/// <param name="PhoneNumberMask">
/// The affected number with only the last four digits retained, or <see langword="null"/> when the
/// number was removed.
/// </param>
/// <param name="Confirmed"><see langword="true"/> when the number was confirmed, <see langword="false"/> when removed.</param>
/// <param name="OccurredAt">When the change occurred (UTC).</param>
public sealed record PhoneChangedEvent(
    string TenantId,
    string UserId,
    string? PhoneNumberMask,
    bool Confirmed,
    DateTimeOffset OccurredAt) : IHuiaEvent;
