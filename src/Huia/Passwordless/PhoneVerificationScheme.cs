namespace Huia.Passwordless;

/// <summary>
/// The cookie authentication scheme and claim types used to carry pending passwordless-phone-sign-in state
/// between <c>PhoneLogin</c>, <c>PhoneLoginVerify</c>, and <c>PhoneLoginConfirmation</c> — the tamper-proof
/// GET-to-POST hand-off for the phone number and, once verified, the user id. See
/// <see cref="Huia.Core.HuiaOptions"/>'s constructor for where the cookie handler is registered, and
/// docs/passwordless.md for the full flow.
/// </summary>
internal static class PhoneVerificationScheme
{
    /// <summary>
    /// The cookie authentication scheme name.
    /// </summary>
    public const string Name = "Huia.PhoneVerification";

    /// <summary>
    /// Claim carrying the E.164-normalized phone number an OTP was sent to. Present from
    /// <c>PhoneLogin.OnPostAsync</c> onward; no longer needed (and not re-issued) once the code is verified.
    /// </summary>
    public const string PhoneNumberClaimType = "phone_number";

    /// <summary>
    /// Claim carrying the <see cref="Identity.HuiaUser"/>'s id the pending sign-in is for.
    /// </summary>
    public const string UserIdClaimType = "user_id";

    /// <summary>
    /// Claim present (value <c>"true"</c>) only after <c>PhoneLoginVerify.OnPostAsync</c> has successfully
    /// verified the OTP for a brand-new registration — required by <c>PhoneLoginConfirmation</c> before it
    /// will collect a name and complete sign-in, so that page can't be reached by forging/replaying a cookie
    /// that never actually passed OTP verification.
    /// </summary>
    public const string VerifiedClaimType = "verified";
}
