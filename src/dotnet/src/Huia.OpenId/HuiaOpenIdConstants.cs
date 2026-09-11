namespace Huia.OpenId;

/// <summary>Constants specific to Huia OpenID Connect and Razor Pages account UI.</summary>
public static class HuiaOpenIdConstants
{
    /// <summary>Login provider name used by passkeys in <c>AspNetUserLogins</c>.</summary>
    public const string PasskeyLoginProvider = "passkey";

    /// <summary>Token name used to record whether passkey enrollment was already prompted.</summary>
    public const string EnrollPromptedTokenName = "enroll_prompted";

    /// <summary>Values written into the <c>amr</c> (Authentication Method Reference) claim array.</summary>
    public static class AuthenticationMethods
    {
        /// <summary>Passkey / WebAuthn ceremony.</summary>
        public const string Passkey = "passkey";
    }

    /// <summary>Cookie name prefixes for interactive flows.</summary>
    public static class Cookies
    {
        /// <summary>The interactive sign-in cookie.</summary>
        public const string Authentication = "huia.auth";

        /// <summary>The short-lived redirect-flow state cookie.</summary>
        public const string Flow = "huia.flow";

        /// <summary>The two-factor pending user cookie.</summary>
        public const string TwoFactorUser = "huia.2fa";

        /// <summary>The anti-forgery protection cookie.</summary>
        public const string AntiForgery = "huia.csrf";
    }

    /// <summary>Claim types emitted on identity principals during external or passkey flows.</summary>
    public static class ClaimTypes
    {
        /// <summary>The upstream external identity provider name.</summary>
        public const string ExternalIdp = "external_idp";

        /// <summary>The upstream raw ID token.</summary>
        public const string ExternalIdToken = "external_id_token";
    }

    /// <summary>OpenIddict application/scope Property dictionary keys.</summary>
    public static class ApplicationProperties
    {
        /// <summary>The tenant owning an application or scope.</summary>
        public const string Tenant = "huia:tenant";

        /// <summary>Whether an application or scope was created statically in code or dynamically via admin API.</summary>
        public const string Origin = "huia:origin";

        /// <summary>The home URIs of an application.</summary>
        public const string HomeUris = "huia:home_uris";

        /// <summary>The client URI of an application.</summary>
        public const string ClientUri = "huia:client_uri";

        /// <summary>The logo URI of an application.</summary>
        public const string LogoUri = "huia:logo_uri";
    }

    /// <summary>Origin values stored in OpenIddict properties.</summary>
    public static class Origins
    {
        /// <summary>Created statically in code / seeding.</summary>
        public const string Static = "static";

        /// <summary>Created dynamically through the admin API.</summary>
        public const string Dynamic = "dynamic";
    }
}
