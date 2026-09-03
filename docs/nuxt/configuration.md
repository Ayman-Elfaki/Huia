# `huia-auth-nuxt` — configuration

## Install

```bash
npm install huia-auth-nuxt
```

In the monorepo the sample apps reference the module from source instead
(`modules: ['../../src/nuxt/src/module']`) plus the runtime deps (`openid-client`, `iron-webcrypto`,
`uncrypto`).

## `nuxt.config.ts`

```ts
export default defineNuxtConfig({
  modules: ['huia-auth-nuxt'],

  huiaAuth: {
    huia: {
      baseUrl: 'https://id.example.com',   // NUXT_HUIA_AUTH_HUIA_BASE_URL
      tenant: 'acme',                      // NUXT_HUIA_AUTH_HUIA_TENANT
      // issuer: 'https://id.example.com/acme',   // overrides baseUrl + tenant
    },
    clientId: 'acme-web',
    // clientSecret via NUXT_HUIA_AUTH_CLIENT_SECRET (confidential client)
    redirectUrl: '/auth/oidc/callback',
    scopes: ['openid', 'profile', 'email', 'offline_access'],   // + 'roles' for an admin app
    allowedAuthParams: ['ui_locales', 'prompt', 'login_hint'],

    par: { enabled: true, required: false },   // `required` mirrors a Huia client with ft:par

    session: {
      name: '__Host-huia_sess',
      // password via NUXT_HUIA_AUTH_SESSION_PASSWORD (>= 32 chars, iron-webcrypto seal key)
      maxAge: 60 * 60 * 24 * 7,
      userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
    },

    storage: { base: 'huia-auth' },

    refresh: {
      enabled: true,
      earlyRefreshSeconds: 60,
      lock: { ttlMs: 10_000, waitMs: 8_000, pollMs: 150 },
    },

    cookie: { chunkSize: 3800, maxChunks: 8 },
    middleware: { global: false, exclude: [] },
  },

  // The token store must be SHARED across instances in a multi-node deployment, or the soft lock
  // and the token record are per-worker and sessions "flap".
  nitro: {
    storage: { 'huia-auth': { driver: 'redis', url: process.env.REDIS_URL } },
    devStorage: { 'huia-auth': { driver: 'fs', base: '.data/huia-auth' } },
  },
})
```

## Environment variables

| Variable | Required | Notes |
|---|---|---|
| `NUXT_HUIA_AUTH_CLIENT_SECRET` | yes | confidential client secret |
| `NUXT_HUIA_AUTH_SESSION_PASSWORD` | yes | ≥ 32 chars; rotating it invalidates every cookie (users re-authenticate) |
| `NUXT_HUIA_AUTH_HUIA_BASE_URL` | — | overrides `huiaAuth.huia.baseUrl` |
| `NUXT_HUIA_AUTH_HUIA_TENANT` | — | overrides `huiaAuth.huia.tenant` |
| `NODE_TLS_REJECT_UNAUTHORIZED=0` | dev only | lets Node accept the ASP.NET Core dev certificate; discovery also honours it for a plain-http issuer in any environment |

`runtimeConfig.public.huiaAuth` contains **only** `{ loginPath, logoutPath, sessionPath }` — no
issuer, client id or secret reaches the browser.

## The Huia token endpoint is form-encoded

OpenIddict accepts `application/x-www-form-urlencoded` only on `/{tenant}/connect/token` and
`/{tenant}/connect/par`. `openid-client` v6 sends form-encoded bodies by design, so there is nothing
to configure — this is called out only because the previous `nuxt-oidc-auth` integration needed an
explicit override.
