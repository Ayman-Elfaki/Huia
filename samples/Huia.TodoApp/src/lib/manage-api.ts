// Set by the AppHost from the todoapi resource's endpoint (see samples/Huia.AppHost) — same origin as
// todo-api.ts's own API_URL, since Huia's self-service manage endpoints are hosted by the same TodoApi
// process that hosts the OIDC server itself.
const API_URL = process.env.TODO_API_URL ?? "http://localhost:5040";

export type ManageInfo = {
    email: string;
    isEmailConfirmed: boolean;
    firstName: string | null;
    lastName: string | null;
};

export type TwoFactorStatus = {
    isTwoFactorEnabled: boolean;
    sharedKey: string | null;
    recoveryCodes: string[] | null;
    recoveryCodesLeft: number;
};

export type ManageSession = {
    id: string;
    createdAt: string;
    lastActivityAt: string;
    expiresAt: string;
    ipAddress: string | null;
    userAgent: string | null;
    revokedAt: string | null;
    isCurrent: boolean;
    applicationClientIds: string[];
};

/** Thrown for a 400 ValidationProblem response — e.g. a wrong current password — so callers can surface
 * field-level errors instead of a generic failure. */
export class ManageApiValidationError extends Error {
    constructor(public readonly errors: Record<string, string[]>) {
        super(Object.values(errors).flat().join(" ") || "Validation failed.");
    }
}

async function manageFetch<T>(path: string, accessToken: string, init?: RequestInit): Promise<T> {
    const response = await fetch(`${API_URL}${path}`, {
        ...init,
        headers: {
            ...init?.headers,
            Authorization: `Bearer ${accessToken}`,
            "Content-Type": "application/json",
        },
        cache: "no-store",
    });

    if (response.status === 400) {
        const problem = await response.json();
        throw new ManageApiValidationError(problem.errors ?? {});
    }

    if (!response.ok) {
        throw new Error(`Manage API request to ${path} failed with ${response.status}: ${await response.text()}`);
    }

    // Revoke endpoints return 204 No Content — response.json() would throw on the empty body, and callers
    // expecting Promise<void> never actually need the parsed value anyway.
    if (response.status === 204) {
        return undefined as T;
    }

    return response.json();
}

export function getInfo(accessToken: string): Promise<ManageInfo> {
    return manageFetch("/api/identity/manage/info", accessToken);
}

export function updateInfo(
    accessToken: string,
    body: { firstName?: string; lastName?: string; newEmail?: string; newPassword?: string; oldPassword?: string },
): Promise<ManageInfo> {
    return manageFetch("/api/identity/manage/info", accessToken, {
        method: "POST",
        body: JSON.stringify(body),
    });
}

/**
 * POSTing an all-empty body returns the current 2FA status without changing anything — unless no
 * authenticator key exists yet, in which case one is generated lazily (the same "get or create" behavior
 * ASP.NET Identity's own manage-2FA page relies on; there's no separate read endpoint). Used to load the
 * panel's initial state.
 */
export function getTwoFactorStatus(accessToken: string): Promise<TwoFactorStatus> {
    return manageFetch("/api/identity/manage/2fa", accessToken, {
        method: "POST",
        body: JSON.stringify({}),
    });
}

export function setTwoFactor(
    accessToken: string,
    body: { enable?: boolean; twoFactorCode?: string; resetSharedKey?: boolean; resetRecoveryCodes?: boolean },
): Promise<TwoFactorStatus> {
    return manageFetch("/api/identity/manage/2fa", accessToken, {
        method: "POST",
        body: JSON.stringify(body),
    });
}

/** Every live/revoked session for the signed-in user, most recently active first — the one backing the
 * caller's own access token is flagged `isCurrent`. */
export function getSessions(accessToken: string): Promise<ManageSession[]> {
    return manageFetch("/api/identity/manage/sessions", accessToken);
}

/** Ends one of the caller's own sessions — signing that device/browser out and notifying every relying
 * party it authorized (see LogoutNotifier). Revoking the current session isn't blocked server-side, but the
 * UI should route that through a normal sign-out instead so this app's own cookie/session gets cleared too. */
export function revokeSession(accessToken: string, sessionId: string): Promise<void> {
    return manageFetch(`/api/identity/manage/sessions/${sessionId}/revoke`, accessToken, { method: "POST" });
}

/** "Sign out of all other devices" — revokes every other live session for the caller, leaving the one
 * making this request untouched. */
export function revokeOtherSessions(accessToken: string): Promise<void> {
    return manageFetch("/api/identity/manage/sessions/revoke-others", accessToken, { method: "POST" });
}
