"use server";

import {revalidatePath} from "next/cache";
import {getServerSession} from "next-auth/next"

import * as todoApi from "@/lib/todo-api";
import {authOptions} from "@/auth";

async function requireAccessToken(): Promise<string> {
    const session = await getServerSession(authOptions);
    if (!session?.accessToken) {
        throw new Error("Not authenticated.");
    }
    return session.accessToken;
}

export async function createTodoAction(formData: FormData): Promise<void> {
    const title = String(formData.get("title") ?? "").trim();
    if (!title) {
        return;
    }

    const accessToken = await requireAccessToken();
    await todoApi.createTodo(accessToken, title);
    revalidatePath("/");
}

export async function toggleTodoAction(id: string, title: string, isComplete: boolean): Promise<void> {
    const accessToken = await requireAccessToken();
    await todoApi.updateTodo(accessToken, id, title, isComplete);
    revalidatePath("/");
}

export async function renameTodoAction(id: string, isComplete: boolean, formData: FormData): Promise<void> {
    const title = String(formData.get("title") ?? "").trim();
    if (!title) {
        return;
    }

    const accessToken = await requireAccessToken();
    await todoApi.updateTodo(accessToken, id, title, isComplete);
    revalidatePath("/");
}

export async function deleteTodoAction(id: string): Promise<void> {
    const accessToken = await requireAccessToken();
    await todoApi.deleteTodo(accessToken, id);
    revalidatePath("/");
}
