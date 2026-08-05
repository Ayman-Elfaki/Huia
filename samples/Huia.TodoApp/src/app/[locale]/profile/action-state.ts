// Split out from actions.ts: a "use server" file can only export async functions — not types (fine, those
// are erased at compile time) but definitely not a plain object like `idleState`.

export type ActionState = {
    status: "idle" | "success" | "error";
    message?: string;
    fieldErrors?: Record<string, string[]>;
};

export const idleState: ActionState = {status: "idle"};

export type TwoFactorState = ActionState & {
    isTwoFactorEnabled?: boolean;
    sharedKey?: string | null;
    recoveryCodes?: string[] | null;
    recoveryCodesLeft?: number;
};
