# Huia Todo sample — web

A [Next.js](https://nextjs.org) (App Router) frontend for the [Huia](https://github.com/Ayman-Elfaki/Huia)
Todo sample. [Auth.js](https://authjs.dev) signs in against `Huia.TodoApi` as a confidential,
server-rendered OIDC client; the todo list is fetched/mutated via React Server Components and Server
Actions calling `TodoApi`'s CRUD endpoints with the session's access token.

See [`../../docs/samples.md`](../../docs/samples.md) for how this fits together with `TodoApi`, Mailpit, and
the Aspire AppHost.

## Running

Normally you'd run this via the AppHost (`aspire run` from `samples/Huia.AppHost`), which wires up
all the environment variables below automatically. To run it standalone instead:

```bash
cp .env.example .env.local
# fill in AUTH_SECRET — generate one with:
npx auth secret
npm run dev
```

This assumes `Huia.TodoApi` is running standalone too, on its default fallback address
(`https://localhost:5041`) — see [`../Huia.TodoApi`](../Huia.TodoApi).

## Environment variables

| Variable | Purpose |
|---|---|
| `AUTH_SECRET` | Encrypts Auth.js's session cookie. |
| `NEXTAUTH_URL` | This app's own base URL — without it, Auth.js infers it from the request and warns on every one. |
| `CACHE_URI` | Redis connection string backing the session store (see `src/lib/session-store.ts`) — the access/id/refresh tokens live here, keyed by session id, instead of in the session cookie itself. |
| `AUTH_HUIA_ISSUER` | `TodoApi`'s OpenIddict issuer — must match its self-reported issuer exactly (including the trailing slash OpenIddict's discovery document normalizes to). |
| `AUTH_HUIA_CLIENT_ID` / `AUTH_HUIA_CLIENT_SECRET` | The `todo-web` client's credentials, as registered by `TodoApi`. |
| `TODO_API_URL` | Base URL for server-side calls to `TodoApi`'s `/api/todos` endpoints. |
