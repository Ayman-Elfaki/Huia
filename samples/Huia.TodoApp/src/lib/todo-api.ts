export type Todo = {
    id: string;
    title: string;
    isComplete: boolean;
    createdAt: string;
};

// Set by the AppHost from the todoapi resource's endpoint (see samples/Huia.AppHost).
const API_URL = process.env.TODO_API_URL ?? "http://localhost:5040";

async function apiFetch(path: string, accessToken: string, init?: RequestInit): Promise<Response> {
    const response = await fetch(`${API_URL}${path}`, {
        ...init,
        headers: {
            ...init?.headers,
            Authorization: `Bearer ${accessToken}`,
            "Content-Type": "application/json",
        },
        cache: "no-store",
    });

    if (!response.ok && response.status !== 404) {
        throw new Error(`TodoApi request to ${path} failed with ${response.status}: ${await response.text()}`);
    }

    return response;
}

// Mirrors MR.AspNetCore.Pagination's KeysetPaginationResult<T> - what GET /api/todos returns (see
// TodoEndpoints.cs). The todo app never paginates, so only `data` (the single page of every todo the
// caller owns) is used.
type KeysetPage<T> = { data: T[] };

export async function listTodos(accessToken: string): Promise<Todo[]> {
    const response = await apiFetch("/api/todos", accessToken);
    const page: KeysetPage<Todo> = await response.json();
    return page.data;
}

export async function createTodo(accessToken: string, title: string): Promise<Todo> {
    const response = await apiFetch("/api/todos", accessToken, {
        method: "POST",
        body: JSON.stringify({ title }),
    });
    return response.json();
}

export async function updateTodo(
    accessToken: string,
    id: string,
    title: string,
    isComplete: boolean,
): Promise<Todo> {
    const response = await apiFetch(`/api/todos/${id}`, accessToken, {
        method: "PUT",
        body: JSON.stringify({ title, isComplete }),
    });
    return response.json();
}

export async function deleteTodo(accessToken: string, id: string): Promise<void> {
    await apiFetch(`/api/todos/${id}`, accessToken, { method: "DELETE" });
}

export type ReportSummary = {
    totalTodos: number;
    completedTodos: number;
    distinctOwners: number;
};

/**
 * Every signed-in user's token carries the "reports" scope (see auth.ts), but ReportsEndpoints also
 * requires the "Admin" role — so a non-admin's token is authenticated fine, just forbidden here. That's a
 * normal, expected outcome for this call (not an API failure), hence the dedicated 403 → null handling
 * rather than reusing apiFetch's throw-on-error behavior.
 */
export async function getReportSummary(accessToken: string): Promise<ReportSummary | null> {
    const response = await fetch(`${API_URL}/api/reports/summary`, {
        headers: { Authorization: `Bearer ${accessToken}` },
        cache: "no-store",
    });

    if (response.status === 403) {
        return null;
    }

    if (!response.ok) {
        throw new Error(`TodoApi request to /api/reports/summary failed with ${response.status}: ${await response.text()}`);
    }

    return response.json();
}
