"use server";

import {getServerSession} from "next-auth/next";
import {revalidatePath} from "next/cache";

import {authOptions} from "@/auth";
import * as manageApi from "@/lib/manage-api";
import {ManageApiValidationError} from "@/lib/manage-api";
import type {ActionState, TwoFactorState} from "@/app/[locale]/profile/action-state";

async function requireAccessToken(): Promise<string> {
    const session = await getServerSession(authOptions);
    if (!session?.accessToken) {
        throw new Error("Not authenticated.");
    }
    return session.accessToken;
}

export async function updateProfileAction(_prev: ActionState, formData: FormData): Promise<ActionState> {
    const accessToken = await requireAccessToken();
    const firstName = String(formData.get("firstName") ?? "").trim();
    const lastName = String(formData.get("lastName") ?? "").trim();

    try {
        await manageApi.updateInfo(accessToken, {firstName, lastName});
        revalidatePath("/profile");
        return {status: "success"};
    } catch (error) {
        if (error instanceof ManageApiValidationError) {
            return {status: "error", fieldErrors: error.errors};
        }
        throw error;
    }
}

export async function changePasswordAction(_prev: ActionState, formData: FormData): Promise<ActionState> {
    const accessToken = await requireAccessToken();
    const oldPassword = String(formData.get("oldPassword") ?? "");
    const newPassword = String(formData.get("newPassword") ?? "");

    try {
        await manageApi.updateInfo(accessToken, {oldPassword, newPassword});
        return {status: "success"};
    } catch (error) {
        if (error instanceof ManageApiValidationError) {
            return {status: "error", fieldErrors: error.errors};
        }
        throw error;
    }
}

/**
 * One dispatcher for all three 2FA forms (enable/disable/regenerate recovery codes) rather than three
 * separate actions — so the panel only needs a single `useActionState` hook, and there's never ambiguity
 * about which of several independent states is the "current" one to render.
 */
export async function twoFactorAction(_prev: TwoFactorState, formData: FormData): Promise<TwoFactorState> {
    const accessToken = await requireAccessToken();
    const intent = String(formData.get("intent") ?? "");

    try {
        let result: manageApi.TwoFactorStatus;
        switch (intent) {
            case "enable": {
                const code = String(formData.get("code") ?? "").trim();
                result = await manageApi.setTwoFactor(accessToken, {enable: true, twoFactorCode: code});
                break;
            }
            case "disable":
                result = await manageApi.setTwoFactor(accessToken, {enable: false});
                break;
            case "regenerate":
                result = await manageApi.setTwoFactor(accessToken, {resetRecoveryCodes: true});
                break;
            default:
                throw new Error(`Unknown 2FA intent: ${intent}`);
        }

        revalidatePath("/profile");
        return {status: "success", ...result};
    } catch (error) {
        if (error instanceof ManageApiValidationError) {
            return {status: "error", fieldErrors: error.errors};
        }
        if (error instanceof Error) {
            return {status: "error", message: error.message};
        }
        throw error;
    }
}
