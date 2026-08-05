import {AuthOptions} from "next-auth";
import {refreshAccessToken, shouldRefresh} from "@/lib/token-refresh";
import {deleteSession, getSession, saveSession} from "@/lib/session-store";

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
            // instead, the same document backchannel-logout/route.ts fetches for jwks_uri.
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
                    sid: profile.sid,
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
            // Fresh sign-in: the access/id/refresh tokens go straight into the session store (see
            // session-store.ts) instead of onto the JWT — the cookie-backed token below only ever carries a
            // reference to that record (Huia's own per-sign-in "sid" claim) plus small, non-sensitive display
            // claims. "offline_access" is in the requested scope above, so Huia issues a refresh_token
            // alongside the access token — account.expires_at is seconds-since-epoch (the OAuth standard
            // unit), converted to ms to match Date.now() below.
            if (account && profile) {
                token.sid = profile.sid;
                token.givenName = profile.given_name;
                token.familyName = profile.family_name;
                token.roles = normalizeRoles(profile.role);
                token.error = undefined;

                await saveSession(token.sid!, {
                    accessToken: account.access_token,
                    idToken: account.id_token,
                    refreshToken: account.refresh_token,
                    accessTokenExpires: account.expires_at ? account.expires_at * 1000 : undefined,
                });
                return token;
            }

            if (!token.sid) {
                // No session id at all — shouldn't happen once signed in, but there's nothing to look up
                // without one.
                return token;
            }

            const stored = await getSession(token.sid);
            if (!stored) {
                // Deleted by /api/auth/backchannel-logout (someone signed this session out elsewhere, or
                // revoked it from the profile page's "Sessions" panel), or evicted/expired — either way,
                // there's nothing left to silently refresh. Treated exactly like a dead refresh token.
                return {...token, error: "RefreshAccessTokenError"};
            }

            // Runs on every request that touches the session (not just sign-in), so this is where an
            // access token nearing expiry gets silently swapped for a fresh one via the refresh_token grant.
            if (!shouldRefresh(stored.accessTokenExpires, Date.now())) {
                return token;
            }

            const refreshed = await refreshAccessToken(stored);
            await saveSession(token.sid, refreshed);
            return {...token, error: refreshed.error};
        },
        async session({session, token}) {
            session.sid = token.sid;
            session.givenName = token.givenName;
            session.familyName = token.familyName;
            session.roles = token.roles;
            // Set only once the stored refresh token is dead (revoked session, expired, etc.) — the page
            // treats this the same as "no session" rather than serving API calls with a token that's about
            // to start failing.
            session.error = token.error;

            // The actual tokens live in the session store, not the JWT — see the `jwt` callback above.
            const stored = token.sid && !token.error ? await getSession(token.sid) : null;
            session.accessToken = stored?.accessToken;
            session.idToken = stored?.idToken;
            session.accessTokenExpires = stored?.accessTokenExpires;
            session.hasRefreshToken = Boolean(stored?.refreshToken);

            return session
        }
    },
    events: {
        // SignOutButton's own normal sign-out flow never reaches /api/auth/backchannel-logout: Huia's
        // LogoutNotifier deliberately excludes whichever client initiated the RP-initiated logout (see its
        // own doc comment) — that client already knows it's signing out, this is it finding out. Without
        // this, the session store entry would just sit there until its own TTL expires instead of being
        // cleaned up immediately.
        async signOut({token}) {
            if (token.sid) {
                await deleteSession(token.sid);
            }
        },
    },
}
