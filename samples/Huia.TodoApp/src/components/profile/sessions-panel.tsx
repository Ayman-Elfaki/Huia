"use client";

import {useState, useTransition} from "react";
import {useTranslations} from "next-intl";

import {revokeOtherSessionsAction, revokeSessionAction} from "@/app/[locale]/profile/actions";
import type {ManageSession} from "@/lib/manage-api";
import {Badge} from "@/components/ui/badge";
import {Button} from "@/components/ui/button";
import {Separator} from "@/components/ui/separator";

function formatDate(value: string): string {
    return new Date(value).toLocaleString();
}

export function SessionsPanel({initial}: { initial: ManageSession[] }) {
    const t = useTranslations("Profile");
    const [error, setError] = useState<string | null>(null);
    const [pendingId, setPendingId] = useState<string | null>(null);
    const [isPending, startTransition] = useTransition();

    const sessions = initial.filter((session) => !session.revokedAt);
    const hasOtherSessions = sessions.some((session) => !session.isCurrent);

    function revoke(sessionId: string) {
        setError(null);
        setPendingId(sessionId);
        startTransition(async () => {
            const result = await revokeSessionAction(sessionId);
            setPendingId(null);
            if (result.status === "error") {
                setError(result.message ?? t("sessionsActionFailed"));
            }
        });
    }

    function revokeOthers() {
        setError(null);
        startTransition(async () => {
            const result = await revokeOtherSessionsAction();
            if (result.status === "error") {
                setError(result.message ?? t("sessionsActionFailed"));
            }
        });
    }

    return (
        <div className="flex flex-col gap-4">
            <p className="text-sm text-muted-foreground">{t("sessionsSubtitle")}</p>

            {error ? <p className="text-sm text-destructive">{error}</p> : null}

            <ul className="flex flex-col gap-3">
                {sessions.map((session, index) => (
                    <li key={session.id} className="flex flex-col gap-2">
                        {index > 0 ? <Separator /> : null}
                        <div className="flex items-center justify-between gap-2">
                            <div className="flex flex-col gap-1">
                                <div className="flex items-center gap-2">
                                    <span className="text-sm font-medium">
                                        {session.applicationClientIds.join(", ") || session.ipAddress || session.id}
                                    </span>
                                    {session.isCurrent ? <Badge variant="secondary">{t("thisDevice")}</Badge> : null}
                                </div>
                                <span className="text-xs text-muted-foreground">
                                    {t("lastActive", {date: formatDate(session.lastActivityAt)})}
                                </span>
                                {session.userAgent ? (
                                    <span className="text-xs text-muted-foreground truncate max-w-sm">
                                        {session.userAgent}
                                    </span>
                                ) : null}
                            </div>
                            {!session.isCurrent ? (
                                <Button
                                    type="button"
                                    variant="outline"
                                    size="sm"
                                    disabled={isPending && pendingId === session.id}
                                    onClick={() => revoke(session.id)}
                                >
                                    {t("signOutSession")}
                                </Button>
                            ) : null}
                        </div>
                    </li>
                ))}
            </ul>

            {hasOtherSessions ? (
                <Button type="button" variant="outline" size="sm" disabled={isPending} onClick={revokeOthers}
                        className="self-start">
                    {t("signOutOtherSessions")}
                </Button>
            ) : null}
        </div>
    );
}
