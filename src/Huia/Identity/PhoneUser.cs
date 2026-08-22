namespace Huia.Identity;

/// <summary>
/// A phone-only, passwordless <see cref="HuiaUser"/> — signs in solely via OTP proof of
/// <see cref="Microsoft.AspNetCore.Identity.IdentityUser.PhoneNumber"/>, never has a password or an external
/// login. Created by the phone sign-in flow (<c>PhoneLoginModel</c>) or by an admin via
/// <c>UsersEndpoints.CreatePhoneUserAsync</c>. Mapped to its own table (<c>HuiaPhoneUsers</c>) via EF Core's
/// table-per-concrete-type (TPC) inheritance strategy — see <c>HuiaDbContext</c>. Inherit from this to add
/// custom claims to phone-only accounts specifically.
/// </summary>
public class PhoneUser : HuiaUser;
