import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
    return twMerge(clsx(inputs))
}


/**
 * Huia's own sign-in cookie is entirely separate from this app's session, so `signOut()` alone only clears
 * the local one — a user who signs out and back in gets silently re-authenticated by Huia's still-live
 * session (SSO) without ever seeing a login prompt. Resolves the OpenID Connect RP-initiated logout URL
 * (`/connect/logout`); the sign-out action redirects the browser there after clearing the local session, so
 * Huia clears its own cookie too before bouncing back to `AUTH_HUIA_POST_LOGOUT_REDIRECT_URI`.
 */
export function huiaEndSessionUrl(idToken: string | undefined): string | null {
    const issuer = process.env.AUTH_HUIA_ISSUER;
    if (!issuer || !idToken) return null;

    const postLogoutRedirectUri = process.env.AUTH_HUIA_POST_LOGOUT_REDIRECT_URI ?? "http://localhost:3000";
    const url = new URL("connect/logout", issuer);
    url.searchParams.set("id_token_hint", idToken);
    url.searchParams.set("post_logout_redirect_uri", postLogoutRedirectUri);
    return url.toString();
}
