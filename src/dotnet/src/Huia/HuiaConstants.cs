namespace Huia;

/// <summary>
/// Well-known names, claim types, authorization policy names, schemes and property keys used
/// across Huia. These values are part of the public contract and change only with a major version bump.
/// </summary>
public static class HuiaConstants
{
    /// <summary>The configuration section Huia binds its options from by default.</summary>
    public const string ConfigurationSection = "Huia";

    /// <summary>Login provider name used for Huia-issued user tokens (for example passwordless OTP secrets).</summary>
    public const string PasswordlessLoginProvider = "Huia.Passwordless";

    /// <summary>Token name, under <see cref="PasswordlessLoginProvider"/>, that stores the hashed one-time code.</summary>
    public const string OtpTokenName = "otp";

    /// <summary>Names of the authentication schemes Huia registers.</summary>
    public static class Schemes
    {
        /// <summary>Policy scheme forwarding to the active flavor's bearer authentication scheme.</summary>
        public const string Api = "Huia:Api";
    }

    /// <summary>Names of the authorization policies Huia registers.</summary>
    public static class Policies
    {
        /// <summary>Policy applied to the token-protected self-service (<c>/manage</c>) API surface.</summary>
        public const string Api = "Huia:Api";
    }

    /// <summary>Names of the roles Huia understands out of the box.</summary>
    public static class Roles
    {
        /// <summary>Role granting access to the administrative endpoints.</summary>
        public const string Administrator = "huia.administrator";
    }

    /// <summary>Claim types Huia reads from, or writes to, principals and tokens.</summary>
    public static class ClaimTypes
    {
        /// <summary>The tenant a principal belongs to.</summary>
        public const string Tenant = "tenant";

        /// <summary>Authentication methods reference (for example <c>pwd</c> or <c>sms</c>).</summary>
        public const string AuthenticationMethod = "amr";

        /// <summary>The user's given name.</summary>
        public const string GivenName = "given_name";

        /// <summary>The user's family name.</summary>
        public const string FamilyName = "family_name";
    }

    /// <summary>Values written to entity origin fields (roles, clients, scopes).</summary>
    public static class Origins
    {
        /// <summary>Seeded from the options tree; read-only.</summary>
        public const string Static = "static";

        /// <summary>Created at runtime through the admin API; editable and deletable.</summary>
        public const string Dynamic = "dynamic";
    }

    /// <summary>Values used for the <see cref="ClaimTypes.AuthenticationMethod"/> claim.</summary>
    public static class AuthenticationMethods
    {
        /// <summary>Interactive password sign-in.</summary>
        public const string Password = "pwd";

        /// <summary>Passwordless SMS one-time code.</summary>
        public const string Sms = "sms";

        /// <summary>Passkey (WebAuthn / FIDO2) assertion.</summary>
        public const string Passkey = "passkey";
    }
}
