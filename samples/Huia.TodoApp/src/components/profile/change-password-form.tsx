"use client";

import {useActionState} from "react";
import {useTranslations} from "next-intl";

import {changePasswordAction} from "@/app/[locale]/profile/actions";
import {idleState} from "@/app/[locale]/profile/action-state";
import {Button} from "@/components/ui/button";
import {Input} from "@/components/ui/input";
import {Label} from "@/components/ui/label";

export function ChangePasswordForm() {
    const t = useTranslations("Profile");
    const [state, formAction, isPending] = useActionState(changePasswordAction, idleState);

    return (
        <form
            // Clears the (never-refilled) password fields after every submit, success or failure — a stale
            // password value sitting in the DOM is the one thing here actually worth resetting on error too.
            key={state.status === "idle" ? "idle" : Math.random()}
            action={formAction}
            className="flex flex-col gap-4"
        >
            <div className="flex flex-col gap-2">
                <Label htmlFor="oldPassword">{t("currentPassword")}</Label>
                <Input id="oldPassword" name="oldPassword" type="password" required autoComplete="current-password" />
            </div>
            <div className="flex flex-col gap-2">
                <Label htmlFor="newPassword">{t("newPassword")}</Label>
                <Input id="newPassword" name="newPassword" type="password" required autoComplete="new-password" />
            </div>
            {state.status === "success" ? <p className="text-sm text-muted-foreground">{t("passwordChanged")}</p> : null}
            {state.status === "error" ? (
                <p className="text-sm text-destructive">
                    {state.fieldErrors?.oldPassword?.[0] ?? t("passwordChangeFailed")}
                </p>
            ) : null}
            <Button type="submit" disabled={isPending} className="self-start">
                {t("changePassword")}
            </Button>
        </form>
    );
}
