// Refreshing a little before the access token's real expiry avoids a request racing the token dying
// mid-flight.
const REFRESH_SKEW_MS = 30_000;

export interface RefreshableToken {
    accessToken?: string;
    idToken?: string;
    refreshToken?: string;
    accessTokenExpires?: number;
    error?: string;
}

/** True once `now` is within `REFRESH_SKEW_MS` of `expiresAt` (or already past it). */
export function shouldRefresh(expiresAt: number | undefined, now: number): boolean {
    if (expiresAt === undefined) {
        return false;
    }

    return now >= expiresAt - REFRESH_SKEW_MS;
}

/**
 * Redeems the stored refresh token against Huia's token endpoint (see TokenEndpoints.cs's
 * HandleReAuthenticateAsync, which handles authorization-code exchange, refresh-token redemption, and
 * device-code polling identically). Returns a token carrying the rotated credentials, or the original token
 * with `error` set if the refresh token is no longer valid, which the caller should treat as "sign in again."
 */
// Generic over T (constrained to RefreshableToken) rather than fixed to that interface, so callers passing
// NextAuth's own JWT type (a RefreshableToken plus an index signature and other fields) get that same type
// back — required for this to slot into an `AuthOptions.callbacks.jwt` implementation, which must return a
// JWT, not just anything shaped like one.
export async function refreshAccessToken<T extends RefreshableToken>(token: T): Promise<T> {
    if (!token.refreshToken) {
        return {...token, error: "RefreshAccessTokenError"} as T;
    }

    try {
        const tokenUrl = new URL("connect/token", process.env.AUTH_HUIA_ISSUER).toString();
        const response = await fetch(tokenUrl, {
            method: "POST",
            headers: {"Content-Type": "application/x-www-form-urlencoded"},
            body: new URLSearchParams({
                grant_type: "refresh_token",
                refresh_token: token.refreshToken,
                client_id: process.env.AUTH_HUIA_CLIENT_ID!,
                client_secret: process.env.AUTH_HUIA_CLIENT_SECRET!,
            }),
        });

        const refreshed = await response.json();
        if (!response.ok) {
            throw new Error(refreshed.error_description ?? refreshed.error ?? "Refresh failed");
        }

        return {
            ...token,
            accessToken: refreshed.access_token,
            idToken: refreshed.id_token ?? token.idToken,
            // OpenIddict rotates refresh tokens by default — always trust whatever came back, only falling
            // back to the one just redeemed if the response omitted one.
            refreshToken: refreshed.refresh_token ?? token.refreshToken,
            accessTokenExpires: Date.now() + refreshed.expires_in * 1000,
            error: undefined,
        } as T;
    } catch {
        return {...token, error: "RefreshAccessTokenError"} as T;
    }
}
