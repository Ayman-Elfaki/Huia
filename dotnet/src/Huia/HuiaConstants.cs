namespace Huia;

/// <summary>
/// Well-known names, claim types, cookie names, authorization policy names and property keys used
/// across the Huia identity provider. These values are part of the public contract and change only
/// with a major version bump.
/// </summary>
public static class HuiaConstants
{
    /// <summary>The configuration section Huia binds its options from by default.</summary>
    public const string ConfigurationSection = "Huia";

    /// <summary>Login provider name used for Huia-issued user tokens (for example passwordless OTP secrets).</summary>
    public const string PasswordlessLoginProvider = "Huia.Passwordless";

    /// <summary>Token name, under <see cref="PasswordlessLoginProvider"/>, that stores the hashed one-time code.</summary>
    public const string OtpTokenName = "otp";

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

    /// <summary>Cookie names issued by Huia. All are prefixed <c>huia.</c> so hosts can reason about them as a set.</summary>
    public static class Cookies
    {
        /// <summary>The interactive login session cookie. <c>SameSite=Lax</c> so it survives the RP&#8594;IdP top-level navigation.</summary>
        public const string Authentication = "huia.auth";

        /// <summary>The flow-state cookie (protected return URLs and pending-flow payloads). <c>SameSite=Strict</c>.</summary>
        public const string Flow = "huia.flow";

        /// <summary>The antiforgery cookie. <c>SameSite=Strict</c>, <c>HttpOnly</c>.</summary>
        public const string AntiForgery = "huia.csrf";
    }

    /// <summary>Claim types Huia reads from, or writes to, principals and tokens.</summary>
    public static class ClaimTypes
    {
        /// <summary>The tenant a principal belongs to.</summary>
        public const string Tenant = "tenant";

        /// <summary>Authentication methods reference (for example <c>pwd</c> or <c>sms</c>).</summary>
        public const string AuthenticationMethod = "amr";

        /// <summary>
        /// The external provider registration id (<c>{tenant}:{provider}</c>) a federated session came
        /// through. Present on the account cookie only for external sign-ins; drives sign-out
        /// propagation to the upstream provider.
        /// </summary>
        public const string ExternalIdp = "huia:ext_idp";

        /// <summary>The upstream provider's id token, kept as the <c>id_token_hint</c> for sign-out.</summary>
        public const string ExternalIdToken = "huia:ext_id_token";

        /// <summary>The user's given name.</summary>
        public const string GivenName = "given_name";

        /// <summary>The user's family name.</summary>
        public const string FamilyName = "family_name";
    }

    /// <summary>
    /// Keys written into an OpenIddict application's <c>Properties</c> dictionary. Because the Huia stores
    /// run with no ambient tenant, the tenant binding of a client lives here rather than in a column.
    /// </summary>
    public static class ApplicationProperties
    {
        /// <summary>The tenant identifier a client is bound to.</summary>
        public const string Tenant = "huia:tenant";

        /// <summary>JSON string array of "home" URIs; the first entry is the sign-out fall-back target.</summary>
        public const string HomeUris = "huia:home_uris";

        /// <summary>The client application's home page URI (OIDC <c>client_uri</c> metadata).</summary>
        public const string ClientUri = "huia:client_uri";

        /// <summary>The client application's logo URI (OIDC <c>logo_uri</c> metadata).</summary>
        public const string LogoUri = "huia:logo_uri";

        /// <summary>
        /// Whether the client or scope was defined in code (seeded from the options tree, and therefore
        /// read-only) or created at runtime through the admin API. One of <see cref="Origins.Static"/> or
        /// <see cref="Origins.Dynamic"/>. A missing value is treated as <see cref="Origins.Static"/>.
        /// </summary>
        public const string Origin = "huia:origin";
    }

    /// <summary>Values written to the <see cref="ApplicationProperties.Origin"/> property.</summary>
    public static class Origins
    {
        /// <summary>Seeded from the options tree by <c>HuiaClientSeeder</c> / <c>HuiaScopeSeeder</c>; read-only.</summary>
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
    }
}
