import {AuthOptions} from "next-auth";
import {refreshAccessToken, shouldRefresh} from "@/lib/token-refresh";

// OpenIddict's role claim type is "role" (singular) — a token with more than one role serializes it as a
// JSON array under that same key rather than pluralizing the key itself, so a raw decoded claim can be a
// single string, an array, or absent. Normalized to an array here so the rest of the app only ever deals
// with `roles: string[]`.
function normalizeRoles(role: string | string[] | undefined): string[] {
    if (role === undefined) return [];
    return Array.isArray(role) ? role : [role];
}

/**
 * Auth.js config for the "todo-web" client registered by Huia.TodoApi (see its Program.cs). The
 * Next.js server holds the client secret and never exposes it to the browser, so this is registered with
 * Huia as a confidential server-side web app rather than a public SPA/PKCE client.
 */

export const authOptions: AuthOptions = {
    // Configure one or more authentication providers
    providers: [
        {
            id: "huia",
            name: "Huia",
            type: "oauth",
            issuer: process.env.AUTH_HUIA_ISSUER,
            // Without this, next-auth skips discovery entirely and builds the OAuth client from
            // provider.authorization/token/userinfo/jwks_endpoint directly — none of which are set below
            // (only authorization.params is), so every sign-in attempt fails with an OAuthSignin error before
            // ever reaching Huia. wellKnown makes it fetch these endpoints from Huia's own discovery document
            // instead.
            wellKnown: process.env.AUTH_HUIA_ISSUER
                ? new URL(".well-known/openid-configuration", process.env.AUTH_HUIA_ISSUER).toString()
                : undefined,
            clientId: process.env.AUTH_HUIA_CLIENT_ID,
            clientSecret: process.env.AUTH_HUIA_CLIENT_SECRET,
            // roles so the id_token carries the caller's role claims (see ClaimsHelpers' scope-gated
            // destinations on the Huia side); reports so this client can reach ReportsEndpoints — whether an
            // individual caller's token actually clears its "Admin" role check is a separate, per-request
            // authorization decision on the API side, not something granting the scope decides.
            authorization: {params: {scope: "openid profile email offline_access todos roles reports"}},
            idToken: true,
            checks: ["pkce", "state"],
            profile(profile) {
                return {
                    id: profile.sub,
                    name: profile.name,
                    email: profile.email,
                    given_name: profile.given_name,
                    family_name: profile.family_name
                }
            },
        },
    ],
    callbacks: {
        async jwt({token, account, profile}) {
            // Persist the OAuth access/id/refresh tokens to the JWT right after signin. "offline_access" is
            // in the requested scope above, so Huia issues a refresh_token alongside the access token —
            // account.expires_at is seconds-since-epoch (the OAuth standard unit), converted to ms to match
            // Date.now() below.
            if (account) {
                token.accessToken = account.access_token;
                token.idToken = account.id_token;
                token.refreshToken = account.refresh_token;
                token.accessTokenExpires = account.expires_at ? account.expires_at * 1000 : undefined;
                token.error = undefined;
            }
            // `profile` is Huia's raw OIDC claims (the provider's own `profile()` above only shapes the
            // *User* object, which the default token below doesn't carry over) — copy the ones the UI needs
            // onto the JWT so they survive as part of the session cookie.
            if (profile) {
                token.givenName = profile.given_name;
                token.familyName = profile.family_name;
                token.roles = normalizeRoles(profile.role);
            }

            // Runs on every request that touches the session (not just sign-in), so this is where an
            // access token nearing expiry gets silently swapped for a fresh one via the refresh_token grant.
            if (!shouldRefresh(token.accessTokenExpires, Date.now())) {
                return token;
            }

            return refreshAccessToken(token);
        },
        async session({session, token}) {
            // Send properties to the client, like an access_token from a provider.
            session.accessToken = token.accessToken;
            session.idToken = token.idToken;
            session.givenName = token.givenName;
            session.familyName = token.familyName;
            session.roles = token.roles;
            session.accessTokenExpires = token.accessTokenExpires;
            session.hasRefreshToken = Boolean(token.refreshToken);
            // Set only once the stored refresh token is dead (expired, etc.) — the page treats this the
            // same as "no session" rather than serving API calls with a token that's about to start
            // failing.
            session.error = token.error;

            return session
        }
    },
}
