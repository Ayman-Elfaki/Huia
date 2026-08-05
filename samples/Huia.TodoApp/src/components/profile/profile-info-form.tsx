"use client";

import {useActionState} from "react";
import {useTranslations} from "next-intl";

import {updateProfileAction} from "@/app/[locale]/profile/actions";
import {idleState} from "@/app/[locale]/profile/action-state";
import {Button} from "@/components/ui/button";
import {Input} from "@/components/ui/input";
import {Label} from "@/components/ui/label";

export function ProfileInfoForm({email, firstName, lastName}: {
    email: string;
    firstName: string | null;
    lastName: string | null;
}) {
    const t = useTranslations("Profile");
    const [state, formAction, isPending] = useActionState(updateProfileAction, idleState);

    return (
        <form action={formAction} className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
                <Label>{t("email")}</Label>
                <p className="text-sm text-muted-foreground">{email}</p>
            </div>
            <div className="flex flex-col gap-2">
                <Label htmlFor="firstName">{t("firstName")}</Label>
                <Input id="firstName" name="firstName" defaultValue={firstName ?? ""} />
            </div>
            <div className="flex flex-col gap-2">
                <Label htmlFor="lastName">{t("lastName")}</Label>
                <Input id="lastName" name="lastName" defaultValue={lastName ?? ""} />
            </div>
            {state.status === "success" ? <p className="text-sm text-muted-foreground">{t("saved")}</p> : null}
            {state.status === "error" ? <p className="text-sm text-destructive">{t("saveFailed")}</p> : null}
            <Button type="submit" disabled={isPending} className="self-start">
                {t("save")}
            </Button>
        </form>
    );
}
