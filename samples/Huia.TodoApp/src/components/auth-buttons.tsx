"use client";

import {useTranslations} from "next-intl";
import {Button} from "@/components/ui/button";
import {signIn, signOut} from "next-auth/react"

export function SignInButton({locale}: { locale: string }) {
    const t = useTranslations("Auth");

    return (
        <Button
            type="button"
            onClick={() => {
                // ui_locales (OIDC Core §3.1.2.1) carries the app's current language to Huia's /connect/authorize
                // request, so an unauthenticated visit lands on a Login/Register page in the same language, rather
                // than one picked from the browser's Accept-Language header.
                void signIn("huia", undefined, {ui_locales: locale});
            }}
        >
            {t("signIn")}
        </Button>
    );
}

export function SignOutButton({endSessionUrl}: { endSessionUrl: string | null }) {
    const t = useTranslations("Auth");

    return (
        <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={async () => {
                // Clear the local session, then send the browser on to Huia's own RP-initiated logout so its
                // sign-in cookie doesn't silently SSO the user back in on the next "Sign in" click.
                await signOut({redirect: false});
                window.location.href = endSessionUrl ?? "/";
            }}
        >
            {t("signOut")}
        </Button>
    );
}
