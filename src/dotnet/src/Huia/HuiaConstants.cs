namespace Huia;

/// <summary>
/// Well-known names, endpoint names, claim types, cookie names, authorization policy names and property keys used
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

    /// <summary>Login provider name used for Huia-issued passkey-related user tokens.</summary>
    public const string PasskeyLoginProvider = "Huia.Passkey";

    /// <summary>
    /// Token name, under <see cref="PasskeyLoginProvider"/>, set to <c>"1"</c> once an account has been
    /// shown the post-sign-up passkey enrollment interstitial (so it is offered at most once).
    /// </summary>
    public const string EnrollPromptedTokenName = "enroll-prompted";

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

    /// <summary>Endpoint names registered across the Huia identity provider.</summary>
    public static class Endpoints
    {
        /// <summary>The home/root endpoint name.</summary>
        public const string Root = "huia.root";

        /// <summary>Health check endpoint names.</summary>
        public static class Health
        {
            /// <summary>The liveness health probe endpoint name.</summary>
            public const string Live = "huia.health.live";

            /// <summary>The readiness health probe endpoint name.</summary>
            public const string Ready = "huia.health.ready";
        }

        /// <summary>OAuth / OpenID Connect protocol endpoint names.</summary>
        public static class Connect
        {
            /// <summary>The authorization endpoint name.</summary>
            public const string Authorize = "huia.connect.authorize";

            /// <summary>The token endpoint name.</summary>
            public const string Token = "huia.connect.token";

            /// <summary>The userinfo endpoint name.</summary>
            public const string UserInfo = "huia.connect.userinfo";

            /// <summary>The logout endpoint name.</summary>
            public const string Logout = "huia.connect.logout";

            /// <summary>The verify endpoint name.</summary>
            public const string Verify = "huia.connect.verify";
        }

        /// <summary>External identity provider endpoint names.</summary>
        public static class External
        {
            /// <summary>The external provider challenge endpoint name.</summary>
            public const string Challenge = "huia.external.challenge";

            /// <summary>The external provider callback endpoint name.</summary>
            public const string Callback = "huia.external.callback";

            /// <summary>The external login dispatch endpoint name.</summary>
            public const string Dispatch = "huia.external.dispatch";

            /// <summary>The post-logout redirection callback endpoint name.</summary>
            public const string SignOutCallback = "huia.external.signout-callback";
        }

        /// <summary>Passkey (WebAuthn / FIDO2) endpoint names.</summary>
        public static class Passkey
        {
            /// <summary>The passkey assertion options endpoint name.</summary>
            public const string AssertionOptions = "huia.passkey.assertion-options";

            /// <summary>The passkey assertion ceremony endpoint name.</summary>
            public const string Assertion = "huia.passkey.assertion";

            /// <summary>The passkey creation options endpoint name.</summary>
            public const string CreationOptions = "huia.passkey.creation-options";

            /// <summary>The passkey credential listing endpoint name.</summary>
            public const string List = "huia.passkey.list";

            /// <summary>The passkey credential registration endpoint name.</summary>
            public const string Register = "huia.passkey.register";

            /// <summary>The passkey credential rename endpoint name.</summary>
            public const string Rename = "huia.passkey.rename";

            /// <summary>The passkey credential removal endpoint name.</summary>
            public const string Remove = "huia.passkey.remove";
        }

        /// <summary>Token-protected self-service (<c>/manage/*</c>) endpoint names.</summary>
        public static class Manage
        {
            /// <summary>User profile endpoint names.</summary>
            public static class Profile
            {
                /// <summary>The get profile endpoint name.</summary>
                public const string Get = "huia.manage.profile.get";

                /// <summary>The update profile endpoint name.</summary>
                public const string Update = "huia.manage.profile.update";
            }

            /// <summary>Email management endpoint names.</summary>
            public static class Email
            {
                /// <summary>The get email endpoint name.</summary>
                public const string Get = "huia.manage.email.get";

                /// <summary>The change email endpoint name.</summary>
                public const string Update = "huia.manage.email.update";

                /// <summary>The send email confirmation endpoint name.</summary>
                public const string Confirm = "huia.manage.email.confirm";
            }

            /// <summary>Password management endpoint names.</summary>
            public static class Password
            {
                /// <summary>The change password endpoint name.</summary>
                public const string Update = "huia.manage.password.update";
            }

            /// <summary>Phone number management endpoint names.</summary>
            public static class Phone
            {
                /// <summary>The get phone endpoint name.</summary>
                public const string Get = "huia.manage.phone.get";

                /// <summary>The start phone change endpoint name.</summary>
                public const string Update = "huia.manage.phone.update";

                /// <summary>The confirm phone change endpoint name.</summary>
                public const string Confirm = "huia.manage.phone.confirm";

                /// <summary>The remove phone endpoint name.</summary>
                public const string Delete = "huia.manage.phone.delete";
            }

            /// <summary>External logins management endpoint names.</summary>
            public static class ExternalLogins
            {
                /// <summary>The list linked external logins endpoint name.</summary>
                public const string List = "huia.manage.external-logins.list";

                /// <summary>The remove linked external login endpoint name.</summary>
                public const string Delete = "huia.manage.external-logins.delete";
            }
        }

        /// <summary>Administrative (<c>/admin/*</c>) endpoint names.</summary>
        public static class Admin
        {
            /// <summary>Tenants administrative endpoint names.</summary>
            public static class Tenants
            {
                /// <summary>The list tenants endpoint name.</summary>
                public const string List = "huia.admin.tenants.list";
            }

            /// <summary>Users administrative endpoint names.</summary>
            public static class Users
            {
                /// <summary>The list users endpoint name.</summary>
                public const string List = "huia.admin.users.list";

                /// <summary>The get user endpoint name.</summary>
                public const string Get = "huia.admin.users.get";

                /// <summary>The create user endpoint name.</summary>
                public const string Create = "huia.admin.users.create";

                /// <summary>The update user endpoint name.</summary>
                public const string Update = "huia.admin.users.update";

                /// <summary>The delete user endpoint name.</summary>
                public const string Delete = "huia.admin.users.delete";

                /// <summary>The lock user endpoint name.</summary>
                public const string Lock = "huia.admin.users.lock";

                /// <summary>The unlock user endpoint name.</summary>
                public const string Unlock = "huia.admin.users.unlock";

                /// <summary>The verify user email endpoint name.</summary>
                public const string VerifyEmail = "huia.admin.users.verify-email";

                /// <summary>User roles administrative endpoint names.</summary>
                public static class Roles
                {
                    /// <summary>The list user roles endpoint name.</summary>
                    public const string List = "huia.admin.users.roles.list";

                    /// <summary>The add user role endpoint name.</summary>
                    public const string Add = "huia.admin.users.roles.add";

                    /// <summary>The remove user role endpoint name.</summary>
                    public const string Remove = "huia.admin.users.roles.remove";
                }

                /// <summary>User claims administrative endpoint names.</summary>
                public static class Claims
                {
                    /// <summary>The list user claims endpoint name.</summary>
                    public const string List = "huia.admin.users.claims.list";

                    /// <summary>The add user claim endpoint name.</summary>
                    public const string Add = "huia.admin.users.claims.add";

                    /// <summary>The remove user claim endpoint name.</summary>
                    public const string Remove = "huia.admin.users.claims.remove";

                    /// <summary>The remove user claims by query endpoint name.</summary>
                    public const string RemoveByQuery = "huia.admin.users.claims.remove.query";
                }
            }

            /// <summary>Roles administrative endpoint names.</summary>
            public static class Roles
            {
                /// <summary>The list roles endpoint name.</summary>
                public const string List = "huia.admin.roles.list";

                /// <summary>The get role endpoint name.</summary>
                public const string Get = "huia.admin.roles.get";

                /// <summary>The create role endpoint name.</summary>
                public const string Create = "huia.admin.roles.create";

                /// <summary>The update role endpoint name.</summary>
                public const string Update = "huia.admin.roles.update";

                /// <summary>The delete role endpoint name.</summary>
                public const string Delete = "huia.admin.roles.delete";
            }

            /// <summary>Clients administrative endpoint names.</summary>
            public static class Clients
            {
                /// <summary>The list clients endpoint name.</summary>
                public const string List = "huia.admin.clients.list";

                /// <summary>The get client endpoint name.</summary>
                public const string Get = "huia.admin.clients.get";

                /// <summary>The create client endpoint name.</summary>
                public const string Create = "huia.admin.clients.create";

                /// <summary>The update client endpoint name.</summary>
                public const string Update = "huia.admin.clients.update";

                /// <summary>The delete client endpoint name.</summary>
                public const string Delete = "huia.admin.clients.delete";
            }

            /// <summary>Signing keys administrative endpoint names.</summary>
            public static class Keys
            {
                /// <summary>The list keys endpoint name.</summary>
                public const string List = "huia.admin.keys.list";

                /// <summary>The get key endpoint name.</summary>
                public const string Get = "huia.admin.keys.get";

                /// <summary>The create key endpoint name.</summary>
                public const string Create = "huia.admin.keys.create";

                /// <summary>The revoke key endpoint name.</summary>
                public const string Revoke = "huia.admin.keys.revoke";

                /// <summary>The delete key endpoint name.</summary>
                public const string Delete = "huia.admin.keys.delete";
            }

            /// <summary>Scopes administrative endpoint names.</summary>
            public static class Scopes
            {
                /// <summary>The list scopes endpoint name.</summary>
                public const string List = "huia.admin.scopes.list";

                /// <summary>The create scope endpoint name.</summary>
                public const string Create = "huia.admin.scopes.create";

                /// <summary>The update scope endpoint name.</summary>
                public const string Update = "huia.admin.scopes.update";

                /// <summary>The delete scope endpoint name.</summary>
                public const string Delete = "huia.admin.scopes.delete";
            }
        }

        /// <summary>Headless flavor endpoint names.</summary>
        public static class Headless
        {
            /// <summary>The headless user registration endpoint name.</summary>
            public const string Register = "huia.headless.register";

            /// <summary>The headless current user profile endpoint name.</summary>
            public const string Me = "huia.headless.me";

            /// <summary>Headless phone login endpoint names.</summary>
            public static class Phone
            {
                /// <summary>The headless phone sign-in start endpoint name.</summary>
                public const string Start = "huia.headless.phone.start";

                /// <summary>The headless phone sign-in verify endpoint name.</summary>
                public const string Verify = "huia.headless.phone.verify";

                /// <summary>The headless phone profile completion endpoint name.</summary>
                public const string CompleteProfile = "huia.headless.phone.complete-profile";
            }

            /// <summary>Headless passkey endpoint names.</summary>
            public static class Passkey
            {
                /// <summary>The headless passkey assertion options endpoint name.</summary>
                public const string AssertionOptions = "huia.headless.passkey.assertion-options";

                /// <summary>The headless passkey assertion ceremony endpoint name.</summary>
                public const string Assertion = "huia.headless.passkey.assertion";

                /// <summary>The headless passkey creation options endpoint name.</summary>
                public const string CreationOptions = "huia.headless.passkey.creation-options";

                /// <summary>The headless passkey credential listing endpoint name.</summary>
                public const string List = "huia.headless.passkey.list";

                /// <summary>The headless passkey credential registration endpoint name.</summary>
                public const string Register = "huia.headless.passkey.register";

                /// <summary>The headless passkey credential rename endpoint name.</summary>
                public const string Rename = "huia.headless.passkey.rename";

                /// <summary>The headless passkey credential removal endpoint name.</summary>
                public const string Remove = "huia.headless.passkey.remove";
            }

            /// <summary>Headless external provider endpoint names.</summary>
            public static class External
            {
                /// <summary>The headless external provider challenge endpoint name.</summary>
                public const string Challenge = "huia.headless.external.challenge";

                /// <summary>The headless external provider callback endpoint name.</summary>
                public const string Callback = "huia.headless.external.callback";

                /// <summary>The headless external code exchange endpoint name.</summary>
                public const string Exchange = "huia.headless.external.exchange";

                /// <summary>The headless external profile completion endpoint name.</summary>
                public const string CompleteProfile = "huia.headless.external.complete-profile";
            }

            /// <summary>Headless administrative endpoint names.</summary>
            public static class Admin
            {
                /// <summary>Headless users administrative endpoint names.</summary>
                public static class Users
                {
                    /// <summary>The list users endpoint name.</summary>
                    public const string List = "huia.headless.admin.users.list";

                    /// <summary>The get user endpoint name.</summary>
                    public const string Get = "huia.headless.admin.users.get";

                    /// <summary>The create user endpoint name.</summary>
                    public const string Create = "huia.headless.admin.users.create";

                    /// <summary>The update user endpoint name.</summary>
                    public const string Update = "huia.headless.admin.users.update";

                    /// <summary>The delete user endpoint name.</summary>
                    public const string Delete = "huia.headless.admin.users.delete";

                    /// <summary>The lock user endpoint name.</summary>
                    public const string Lock = "huia.headless.admin.users.lock";

                    /// <summary>The unlock user endpoint name.</summary>
                    public const string Unlock = "huia.headless.admin.users.unlock";

                    /// <summary>Headless user roles administrative endpoint names.</summary>
                    public static class Roles
                    {
                        /// <summary>The list user roles endpoint name.</summary>
                        public const string List = "huia.headless.admin.users.roles.list";

                        /// <summary>The add user role endpoint name.</summary>
                        public const string Add = "huia.headless.admin.users.roles.add";

                        /// <summary>The remove user role endpoint name.</summary>
                        public const string Remove = "huia.headless.admin.users.roles.remove";
                    }

                    /// <summary>Headless user claims administrative endpoint names.</summary>
                    public static class Claims
                    {
                        /// <summary>The list user claims endpoint name.</summary>
                        public const string List = "huia.headless.admin.users.claims.list";

                        /// <summary>The add user claim endpoint name.</summary>
                        public const string Add = "huia.headless.admin.users.claims.add";

                        /// <summary>The remove user claim endpoint name.</summary>
                        public const string Remove = "huia.headless.admin.users.claims.remove";

                        /// <summary>The remove user claims by query endpoint name.</summary>
                        public const string RemoveByQuery = "huia.headless.admin.users.claims.remove.query";
                    }
                }

                /// <summary>Headless roles administrative endpoint names.</summary>
                public static class Roles
                {
                    /// <summary>The list roles endpoint name.</summary>
                    public const string List = "huia.headless.admin.roles.list";

                    /// <summary>The get role endpoint name.</summary>
                    public const string Get = "huia.headless.admin.roles.get";

                    /// <summary>The create role endpoint name.</summary>
                    public const string Create = "huia.headless.admin.roles.create";

                    /// <summary>The update role endpoint name.</summary>
                    public const string Update = "huia.headless.admin.roles.update";

                    /// <summary>The delete role endpoint name.</summary>
                    public const string Delete = "huia.headless.admin.roles.delete";
                }
            }
        }
    }

    /// <summary>Cookie names issued by Huia. All are prefixed <c>huia.</c> so hosts can reason about them as a set.</summary>
    public static class Cookies
    {
        /// <summary>The interactive login session cookie. <c>SameSite=Lax</c> so it survives the RP&#8594;IdP top-level navigation.</summary>
        public const string Authentication = "huia.auth";

        /// <summary>The flow-state cookie (protected return URLs and pending-flow payloads). <c>SameSite=Strict</c>.</summary>
        public const string Flow = "huia.flow";

        /// <summary>
        /// The short-lived transient cookie that carries the passkey attestation / assertion ceremony
        /// state (ASP.NET Core Identity's <c>TwoFactorUserId</c> scheme, which its passkey helpers reuse).
        /// <c>SameSite=Lax</c> so it survives the top-level navigation from <c>/connect/authorize</c> to
        /// the login page.
        /// </summary>
        public const string TwoFactorUser = "huia.2fa-user";

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

        /// <summary>Passkey (WebAuthn / FIDO2) assertion.</summary>
        public const string Passkey = "passkey";
    }
}
