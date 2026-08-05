# Huia Admin UI

A [Nuxt](https://nuxt.com) console for managing a [Huia](https://github.com/Ayman-Elfaki/Huia) instance —
applications, scopes, users, roles, live authorizations, and sessions — built entirely on top of
`MapHuiaAdminEndpoints` (see `samples/Huia.TodoApi/Program.cs`, which maps and role-gates it).

[`nuxt-oidc-auth`](https://nuxtoidc.cloud) signs in against `Huia.TodoApi` as a confidential,
server-rendered OIDC client (the `admin-ui` client Program.cs registers) — the same pattern
`Huia.TodoApp`'s Auth.js config uses, just on Nuxt's server instead of Next.js's. The signed-in user's
access token never reaches the browser: every page calls this app's own `/api/admin/**` routes
(`server/api/admin/[...path].ts`), which attach the token server-side and proxy straight through to
`/api/identity/admin/**`. Access is gated on the `Admin` role both in that proxy and, ultimately, by
`MapHuiaAdminEndpoints` itself.

## Running

Normally you'd run this via the AppHost (`aspire run` from `samples/Huia.AppHost`), which wires up all the
environment variables below automatically. To run it standalone instead:

```bash
cp .env.example .env
npm install
npm run dev
```

This assumes `Huia.TodoApi` is running standalone too, on its default fallback address
(`https://localhost:5041`) — see [`../Huia.TodoApi`](../Huia.TodoApi). Sign in with the seeded demo admin
account (`admin@example.com` / `Admin123!Demo`).

## Environment variables

| Variable | Purpose |
|---|---|
| `HUIA_ISSUER` | `TodoApi`'s OpenIddict issuer — used to build the authorize/token/userinfo/logout URLs and to fetch its discovery document for token validation. |
| `HUIA_CLIENT_ID` / `HUIA_CLIENT_SECRET` | The `admin-ui` client's credentials, as registered by `TodoApi`. |
| `HUIA_REDIRECT_URI` | Must match one of the `admin-ui` client's registered redirect URIs — always `<this app's origin>/auth/oidc/callback` (`oidc` is nuxt-oidc-auth's fixed provider key for a generic OIDC provider, not a name we chose). |
| `NUXT_OIDC_SESSION_SECRET` / `NUXT_OIDC_TOKEN_KEY` / `NUXT_OIDC_AUTH_SESSION_SECRET` | Encrypt nuxt-oidc-auth's session/refresh-token cookies. Left unset, the module generates ephemeral ones on boot (fine for a quick local try, but every restart signs everyone out) — set real values for anything longer-lived. |

## Adding a resource page

Each resource page (`app/pages/*/index.vue`) follows the same shape: `useAdminList` (`app/composables/useAdminList.ts`)
fetches a page of `PagedResult<T>` from `/api/admin/<resource>`, `UTable` renders it, and a `UModal` + `UForm`
handles create/edit. `shared/types/admin.ts` mirrors the request/response records in
`src/Huia/Endpoints/Admin/*.cs` — keep the two in sync if Huia's admin API changes shape.
