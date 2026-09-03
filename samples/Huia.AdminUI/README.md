# Huia.AdminUI

A Nuxt 4 admin console for the Huia `master` tenant.

- Confidential `nuxt-oidc-auth` client; requests the `roles` scope and the admin API checks the
  `huia.administrator` role.
- `/` landing, `/tenants` (from `/master/admin/tenants`), `/profile` (via `/master/manage/profile`).
- Real **shadcn-vue** components (`app/components/ui`), a light/dark toggle (`@nuxtjs/color-mode`, dark by default).
- Every Huia / resource-API call goes through **nuxt-api-party**; the access token is added server-side (`server/plugins/api-party-auth.ts`) and never reaches the browser.

```bash
npm install --legacy-peer-deps
npm run dev   # http://localhost:3001
```

Environment: `NUXT_PUBLIC_HUIA_BASE_URL` (default `https://localhost:5310`),
`NUXT_OIDC_PROVIDERS_OIDC_CLIENT_SECRET` (default `huia-admin-ui-secret`),
`NUXT_OIDC_SESSION_SECRET` (32+ chars), `NODE_TLS_REJECT_UNAUTHORIZED=0` in dev.
