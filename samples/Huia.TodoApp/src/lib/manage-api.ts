// Set by the AppHost from the todoapi resource's endpoint (see samples/Huia.AppHost) — same origin as
// todo-api.ts's own API_URL, since Huia's self-service manage endpoints are hosted by the same TodoApi
// process that hosts the OIDC server itself.
const API_URL = process.env.TODO_API_URL ?? "http://localhost:5040";

export type ManageInfo = {
    email: string | null;
    isEmailConfirmed: boolean;
    phoneNumber: string | null;
    isPhoneNumberConfirmed: boolean;
    firstName: string | null;
    lastName: string | null;
};

export type TwoFactorStatus = {
    isTwoFactorEnabled: boolean;
    sharedKey: string | null;
    recoveryCodes: string[] | null;
    recoveryCodesLeft: number;
};

export type ExternalLogins = {
    logins: { loginProvider: string; providerDisplayName: string | null; providerKey: string }[];
    hasPassword: boolean;
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

/** Whether the signed-in account has a local password — false for one signed in only through an external
 * provider (Google, etc.), in which case the change-password and 2FA cards don't apply (see profile page). */
export function getExternalLogins(accessToken: string): Promise<ExternalLogins> {
    return manageFetch("/api/identity/manage/external-logins", accessToken);
}

export function updateInfo(
    accessToken: string,
    body: {
        firstName?: string;
        lastName?: string;
        newEmail?: string;
        newPassword?: string;
        oldPassword?: string;
    },
): Promise<ManageInfo> {
    return manageFetch("/api/identity/manage/info", accessToken, {
        method: "POST",
        body: JSON.stringify(body),
    });
}

/** Step 1 of the OTP-verified phone number change - sends a code to `newPhoneNumber` (must already be fully
 * E.164-qualified, e.g. "+15551234567"). Only works when the app has passwordless phone sign-in enabled. */
export function requestPhoneChange(
    accessToken: string,
    newPhoneNumber: string,
): Promise<{ maskedPhoneNumber: string }> {
    return manageFetch("/api/identity/manage/info/phone", accessToken, {
        method: "POST",
        body: JSON.stringify({ newPhoneNumber }),
    });
}

/** Step 2 - resends the same number alongside the code the user received, since the verification token is
 * bound to that specific number rather than tracked via server-side pending state. */
export function verifyPhoneChange(accessToken: string, newPhoneNumber: string, code: string): Promise<ManageInfo> {
    return manageFetch("/api/identity/manage/info/phone/verify", accessToken, {
        method: "POST",
        body: JSON.stringify({ newPhoneNumber, code }),
    });
}

/** Clears the signed-in user's phone number - no OTP needed, since removing data is lower-risk than
 * adding/changing it. Rejected if it's the account's only sign-in method. */
export function removePhoneNumber(accessToken: string): Promise<ManageInfo> {
    return manageFetch("/api/identity/manage/info/phone", accessToken, { method: "DELETE" });
}

/**
 * POSTing an all-empty body returns the current 2FA status without changing anything — including
 * `sharedKey`, which stays null until enrollment is explicitly started via `setTwoFactor(..., {resetSharedKey:
 * true})` (see two-factor-panel.tsx's "start setup" action). Used to load the panel's initial state; must
 * stay a pure read, since it also runs on every profile page load.
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
