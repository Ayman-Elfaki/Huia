import {getServerSession} from "next-auth/next";
import {getLocale} from "next-intl/server";

import {authOptions} from "@/auth";
import {redirect} from "@/i18n/navigation";

/**
 * Resolves the signed-in user's access token for use in a Server Action. A dead refresh token (revoked,
 * expired, ...) is surfaced as session.error by the jwt callback in auth.ts, but session.accessToken keeps
 * holding the last (now-unusable) value rather than being cleared — without this check, a Server Action
 * would hand that stale token straight to the API and blow up on the resulting 401 instead of sending the
 * user back to the sign-in prompt, which is how page.tsx and profile/page.tsx already treat this same state.
 */
export async function requireAccessToken(): Promise<string> {
    const session = await getServerSession(authOptions);
    if (!session?.accessToken || session.error) {
        return redirect({href: "/", locale: await getLocale()});
    }
    return session.accessToken;
}
