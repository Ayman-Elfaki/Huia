"use client";

import {useActionState} from "react";
import {useTranslations} from "next-intl";

import {twoFactorAction} from "@/app/[locale]/profile/actions";
import type {TwoFactorState} from "@/app/[locale]/profile/action-state";
import {Badge} from "@/components/ui/badge";
import {Button} from "@/components/ui/button";
import {Input} from "@/components/ui/input";
import {Label} from "@/components/ui/label";
import {QrCode} from "@/components/ui/qr-code";
import {Separator} from "@/components/ui/separator";

/** Groups a raw authenticator key into 4-char chunks — matches how authenticator apps display/expect a
 * manually-entered secret. */
function formatSharedKey(key: string): string {
    return (key.match(/.{1,4}/g) ?? [key]).join(" ");
}

export function TwoFactorPanel({email, initial}: { email: string; initial: TwoFactorState }) {
    const t = useTranslations("Profile");
    const [state, formAction, isPending] = useActionState(twoFactorAction, initial);

    const otpauthUrl = state.sharedKey
        ? `otpauth://totp/Huia%20Todo:${encodeURIComponent(email)}?secret=${state.sharedKey}&issuer=Huia%20Todo&digits=6`
        : null;

    return (
        <div className="flex flex-col gap-4">
            <div className="flex items-center gap-2">
                <span className="text-sm font-medium">{t("twoFactorStatus")}</span>
                <Badge variant={state.isTwoFactorEnabled ? "default" : "secondary"}>
                    {state.isTwoFactorEnabled ? t("twoFactorEnabled") : t("twoFactorDisabled")}
                </Badge>
            </div>

            {state.status === "error" ? (
                <p className="text-sm text-destructive">{state.message ?? t("twoFactorActionFailed")}</p>
            ) : null}

            {state.recoveryCodes ? (
                <div className="flex flex-col gap-2 rounded-lg border border-destructive/50 bg-destructive/5 p-3">
                    <p className="text-sm font-medium">{t("recoveryCodesTitle")}</p>
                    <p className="text-xs text-muted-foreground">{t("recoveryCodesHint")}</p>
                    <ul className="grid grid-cols-2 gap-1 font-mono text-xs">
                        {state.recoveryCodes.map((code) => (
                            <li key={code}>{code}</li>
                        ))}
                    </ul>
                </div>
            ) : null}

            {state.isTwoFactorEnabled ? (
                <div className="flex flex-col gap-3">
                    <p className="text-sm text-muted-foreground">
                        {t("recoveryCodesLeft", {count: state.recoveryCodesLeft ?? 0})}
                    </p>
                    <div className="flex flex-wrap gap-2">
                        <form action={formAction}>
                            <input type="hidden" name="intent" value="regenerate" />
                            <Button type="submit" variant="outline" size="sm" disabled={isPending}>
                                {t("regenerateRecoveryCodes")}
                            </Button>
                        </form>
                        <form action={formAction}>
                            <input type="hidden" name="intent" value="disable" />
                            <Button type="submit" variant="outline" size="sm" disabled={isPending}>
                                {t("disableTwoFactor")}
                            </Button>
                        </form>
                    </div>
                </div>
            ) : state.sharedKey ? (
                <div className="flex flex-col gap-3">
                    <div className="flex flex-col gap-2">
                        <p className="text-sm text-muted-foreground">{t("twoFactorSetupHint")}</p>
                        {otpauthUrl ? <QrCode value={otpauthUrl} /> : null}
                        <code className="rounded-md bg-muted px-2 py-1 text-xs break-all">
                            {formatSharedKey(state.sharedKey)}
                        </code>
                        {otpauthUrl ? (
                            <a href={otpauthUrl} className="text-xs text-muted-foreground underline underline-offset-2">
                                {t("openInAuthenticatorApp")}
                            </a>
                        ) : null}
                    </div>

                    <Separator />

                    <form action={formAction} className="flex flex-col gap-3">
                        <input type="hidden" name="intent" value="enable" />
                        <div className="flex flex-col gap-2">
                            <Label htmlFor="code">{t("twoFactorCode")}</Label>
                            <Input id="code" name="code" inputMode="numeric" autoComplete="one-time-code" required />
                        </div>
                        <Button type="submit" disabled={isPending} className="self-start">
                            {t("enableTwoFactor")}
                        </Button>
                    </form>
                </div>
            ) : (
                <div className="flex flex-col gap-3">
                    <p className="text-sm text-muted-foreground">{t("twoFactorSetupPrompt")}</p>
                    <form action={formAction}>
                        <input type="hidden" name="intent" value="start-setup" />
                        <Button type="submit" variant="outline" disabled={isPending} className="self-start">
                            {t("startTwoFactorSetup")}
                        </Button>
                    </form>
                </div>
            )}
        </div>
    );
}
