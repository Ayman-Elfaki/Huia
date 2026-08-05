import Redis from "ioredis";
import type { RefreshableToken } from "@/lib/token-refresh";

/**
 * Server-side store for the access/id/refresh tokens behind a signed-in session, keyed by Huia's own
 * per-sign-in session id (the OIDC `sid` claim — see auth.ts's `jwt` callback). Backed by the "cache" Redis
 * resource (see AppHost.cs), so the tokens themselves never need to round-trip through the browser at all —
 * the session cookie (Auth.js's own encrypted JWT) only ever carries the `sid` reference plus small,
 * non-sensitive display claims.
 *
 * This is also what makes `/api/auth/backchannel-logout` able to actually revoke a session on Huia's
 * say-so: deleting a Redis key is something a self-contained, stateless JWT cookie can never be made to
 * forget on demand (short of waiting out its own expiry) — see deleteSession below.
 */

export type StoredSession = RefreshableToken;

const KEY_PREFIX = "todoapp:session:";

// A stored session outlives any single access/refresh token pair by design (redeeming the refresh token
// repeatedly is the whole point), so it isn't bounded by their lifetimes — this TTL instead bounds how long
// an abandoned session (browser closed without ever signing out, cookie never cleared) lingers in Redis.
// Refreshed on every write, so an actively-used session never hits it.
const SESSION_TTL_SECONDS = 60 * 60 * 24 * 30; // 30 days

let client: Redis | undefined;

function getClient(): Redis {
    if (client) {
        return client;
    }

    const url = process.env.CACHE_URI;
    if (!url) {
        throw new Error(
            "CACHE_URI is not set. Running under the Aspire AppHost sets this automatically (see AppHost.cs's " +
            "\"cache\" Redis resource); for a standalone `npm run dev`, point it at a local Redis instance, " +
            "e.g. CACHE_URI=redis://localhost:6379 in .env.local.",
        );
    }

    client = new Redis(url, {
        // Aspire's container tunnel fronts every container resource (including "cache") behind a self-signed
        // dev certificate — same gap AppHost.cs's NODE_TLS_REJECT_UNAUTHORIZED comment describes for the
        // HTTPS side, scoped here to just this one client instead of the whole process.
        tls: url.startsWith("rediss://") ? { rejectUnauthorized: false } : undefined,
    });

    // ioredis's own retry/reconnect logic already handles a dropped connection; without this listener, an
    // EventEmitter "error" event with nobody listening for it is treated as an uncaught exception by
    // Node — which would crash the whole dev server on a single transient connection error rather than
    // just failing the in-flight request.
    client.on("error", (error) => {
        console.error("[session-store] Redis client error:", error);
    });

    return client;
}

export async function saveSession(sid: string, session: StoredSession): Promise<void> {
    await getClient().set(KEY_PREFIX + sid, JSON.stringify(session), "EX", SESSION_TTL_SECONDS);
}

export async function getSession(sid: string): Promise<StoredSession | null> {
    const raw = await getClient().get(KEY_PREFIX + sid);
    return raw ? (JSON.parse(raw) as StoredSession) : null;
}

/** Called by /api/auth/backchannel-logout whenever Huia notifies this client that a session ended — signing
 * out through another client with a live SSO session here, an admin's revoke, or this app's own user
 * revoking one of their sessions from the profile page's "Sessions" panel (see revokeSessionAction/
 * revokeOtherSessionsAction in @/lib/manage-api) all end up notifying back-channel the same way. A missing
 * key is treated by auth.ts's `jwt` callback exactly like a dead refresh token: the next request signs the
 * session out instead of silently refreshing it. */
export async function deleteSession(sid: string): Promise<void> {
    await getClient().del(KEY_PREFIX + sid);
}
