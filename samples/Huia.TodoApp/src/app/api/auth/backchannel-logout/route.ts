import {createRemoteJWKSet, jwtVerify} from "jose";

/**
 * Receives the OpenID Connect Back-Channel Logout 1.0 notification Huia's LogoutNotifier POSTs here
 * whenever the user signs out through a *different* client (see Huia.TodoApi/Program.cs's
 * SetBackChannelLogoutUri, and LogoutNotifier.cs on the .NET side for what it sends and why "todo-web"
 * itself is excluded when it's the client that initiated the logout — see auth-buttons.tsx's SignOutButton
 * for that other half). Spec: https://openid.net/specs/openid-connect-backchannel-1_0.html
 */

let jwks: ReturnType<typeof createRemoteJWKSet> | undefined;

/** Resolves the current signing keys from Huia's own discovery document rather than hardcoding a jwks_uri path
 — jose caches the keys themselves internally once this is created. */
async function getJwks() {
    if (jwks) {
        return jwks;
    }

    const issuer = process.env.AUTH_HUIA_ISSUER!;
    const discoveryResponse = await fetch(new URL(".well-known/openid-configuration", issuer));
    const discoveryDocument = (await discoveryResponse.json()) as { jwks_uri: string };
    jwks = createRemoteJWKSet(new URL(discoveryDocument.jwks_uri));
    return jwks;
}

function invalidRequest(description: string) {
    return Response.json({error: "invalid_request", error_description: description}, {status: 400});
}

export async function POST(request: Request) {
    const form = await request.formData();
    const logoutToken = form.get("logout_token");
    if (typeof logoutToken !== "string" || !logoutToken) {
        return invalidRequest("Missing logout_token.");
    }

    let payload;
    try {
        const result = await jwtVerify(logoutToken, await getJwks(), {
            issuer: process.env.AUTH_HUIA_ISSUER,
            audience: process.env.AUTH_HUIA_CLIENT_ID,
        });
        payload = result.payload;
    } catch (error) {
        return invalidRequest(`logout_token failed verification: ${error instanceof Error ? error.message : error}`);
    }

    // Both required by the spec: "events" marks this as a logout_token rather than an ordinary ID token, and
    // "nonce" must be absent (a logout_token is never issued in response to an authentication request that
    // would have carried one — its presence would suggest token confusion/replay).
    const events = payload.events as Record<string, unknown> | undefined;
    if (!events?.["http://schemas.openid.net/event/backchannel-logout"]) {
        return invalidRequest("logout_token is missing the backchannel-logout event.");
    }
    if ("nonce" in payload) {
        return invalidRequest("logout_token must not contain a nonce claim.");
    }
    // Huia's logout_token carries sid, not sub (see LogoutNotifier's doc comment on the .NET side) — session
    // storage here is keyed the same way (see session-store.ts), so a sub-only check would never match.
    if (!payload.sid || typeof payload.sid !== "string") {
        return invalidRequest("logout_token is missing a sid claim.");
    }

    // Cache-Control: no-store per the spec, so no intermediary caches this response for a subsequent
    // unrelated request.
    return new Response(null, {status: 200, headers: {"Cache-Control": "no-store"}});
}
