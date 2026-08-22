namespace Huia.Identity;

/// <summary>
/// A <see cref="HuiaUser"/> that signs in via email/password and/or an external login provider. May also
/// record and verify a phone number (<see cref="HuiaUser.NormalizedPhoneNumber"/>) for account management
/// purposes without becoming phone-authenticated — that's what distinguishes it from
/// <see cref="Huia.Identity.PhoneUser"/>, whose phone number IS its sign-in credential. Mapped to its own
/// table (<c>HuiaUsers</c>, preserving the name used before the <see cref="Huia.Identity.PhoneUser"/> split)
/// via EF Core's table-per-concrete-type (TPC) inheritance strategy — see <c>HuiaDbContext</c>. Inherit from
/// this to add custom claims to standard accounts specifically.
/// </summary>
public class StandardUser : HuiaUser;
