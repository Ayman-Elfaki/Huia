# Todo.App

A Nuxt 4 front-end for the Huia `todo` tenant.

- Signs in with `nuxt-oidc-auth` (generic `oidc` provider) as a **confidential** client — the code
  exchange and the access token stay on the Nitro server.
- `/` is a public landing page; `/tasks` (Todo CRUD against `Todo.Api`) and `/profile` (read/update
  via `/{tenant}/manage/profile`) require a session.
- Real **shadcn-vue** components (`app/components/ui`), a light/dark toggle (`@nuxtjs/color-mode`, dark by default).
- Every Huia / resource-API call goes through **nuxt-api-party**; the access token is added server-side (`server/plugins/api-party-auth.ts`) and never reaches the browser.

```bash
npm install --legacy-peer-deps
npm run dev   # http://localhost:3000
```

Environment:

| Variable | Default |
|---|---|
| `NUXT_PUBLIC_HUIA_BASE_URL` | `https://localhost:5310` |
| `NUXT_PUBLIC_TODO_API_URL` | `http://localhost:5330` |
| `NUXT_OIDC_PROVIDERS_OIDC_CLIENT_SECRET` | `todo-app-secret` |
| `NUXT_OIDC_SESSION_SECRET` | (32+ chars, required by nuxt-oidc-auth) |
| `NODE_TLS_REJECT_UNAUTHORIZED` | set to `0` in dev when Huia uses the ASP.NET dev cert |
