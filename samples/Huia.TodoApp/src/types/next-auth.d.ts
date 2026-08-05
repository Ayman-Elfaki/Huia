// Module augmentation has to target the exact module specifier that declares the interface being extended.
// next-auth v4 declares `Session`/`Profile` itself (in "next-auth"/"next-auth/core/types", re-exported from
// the "next-auth" entrypoint) and `JWT` in "next-auth/jwt" — not "@auth/core/*", which this package doesn't
// even depend on, so augmenting that silently merges with nothing.
declare module "next-auth" {
  interface Session {
    // Populated by the `session` callback in auth.ts from the session store (see session-store.ts) — not
    // from the JWT itself, which no longer carries these (see the `jwt` callback's own remarks).
    accessToken?: string;
    idToken?: string;
    sub?: string;
    // Huia's own per-sign-in session id (the OIDC `sid` claim) — what back-channel logout notifications
    // are keyed by (see backchannel-logout/route.ts); sub is kept above only for display.
    sid?: string;
    givenName?: string;
    familyName?: string;
    roles?: string[];
    // Milliseconds-since-epoch — surfaced (not sensitive, just a timestamp) so the UI can show when the
    // access token was last (re)issued/will next be silently refreshed by the `jwt` callback below.
    accessTokenExpires?: number;
    // Whether a refresh_token is on file server-side — never the token itself, which stays server-only.
    hasRefreshToken?: boolean;
    // Set by the `jwt` callback in auth.ts when the stored refresh token can no longer redeem a fresh
    // access token — the page treats this the same as "no session."
    error?: string;
  }

  interface Profile {
    // Huia's raw OIDC claims beyond the base sub/name/email/image (see auth.ts's `profile()` mapping and
    // its `jwt` callback, which both read these). OpenIddict's role claim type is "role" (singular,
    // OpenIddictConstants.Claims.Role) — a JWT with more than one role claim serializes it as a JSON array
    // under that same singular key, so this is `string | string[]`, never a `roles` field.
    sid?: string;
    given_name?: string;
    family_name?: string;
    role?: string | string[];
  }
}

declare module "next-auth/jwt" {
  interface JWT {
    // Huia's own per-sign-in session id (the OIDC `sid` claim) — the only thing this JWT needs to carry to
    // look the rest up in the session store (see session-store.ts/auth.ts's `jwt` callback). The actual
    // access/id/refresh tokens deliberately do NOT live here anymore: keeping them out of the cookie is the
    // whole point of the session store, and it's also what lets /api/auth/backchannel-logout actually
    // revoke a session (deleting a Redis key) instead of a stateless cookie living out its own expiry.
    sid?: string;
    givenName?: string;
    familyName?: string;
    roles?: string[];
    error?: string;
  }
}

export {};
