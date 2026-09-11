# `nuxt-huia-headless` — configuration

## Install

```bash
npm install nuxt-huia-headless
```

In the monorepo the `Shop.App` sample references the module from source instead
(`modules: ['../../src/nuxt/nuxt-huia-headless/src/module']`).

## `nuxt.config.ts`

```ts
export default defineNuxtConfig({
  modules: ['nuxt-huia-headless'],

  huiaHeadless: {
    baseUrl: 'https://api.example.com',   // NUXT_HUIA_HEADLESS_BASE_URL

    session: {
      name: '__Host-huia_headless_sess',
      // password via NUXT_HUIA_HEADLESS_SESSION_PASSWORD (>= 32 chars, iron-webcrypto seal key)
      maxAge: 60 * 60 * 24 * 7,
      userClaims: ['sub', 'email', 'firstName', 'lastName', 'roles'],
    },

    storage: { base: 'huia-headless-auth' },

    refresh: {
      enabled: true,
      earlyRefreshSeconds: 60,
      lock: { ttlMs: 10_000, waitMs: 8_000, pollMs: 150 },
    },

    cookie: { chunkSize: 3800, maxChunks: 8 },

    // No hosted login page to redirect to — an unauthenticated visit to a protected route goes to
    // the app's own loginPage instead of an external URL.
    loginPage: '/login',
    middleware: { global: false, exclude: [] },

    allowInsecureTls: false,   // dev only
  },

  nitro: {
    storage: { 'huia-headless-auth': { driver: 'redis', url: process.env.REDIS_URL } },
    devStorage: { 'huia-headless-auth': { driver: 'fs', base: '.data/huia-headless-auth' } },
  },
})
```

## Environment variables

| Variable | Required | Notes |
|---|---|---|
| `NUXT_HUIA_HEADLESS_SESSION_PASSWORD` | yes | ≥ 32 chars. Rotating it invalidates every existing cookie. |
| `NUXT_HUIA_HEADLESS_BASE_URL` | — | overrides `huiaHeadless.baseUrl` |
| `NODE_TLS_REJECT_UNAUTHORIZED=0` | dev only | lets Node accept the ASP.NET Core dev certificate for the `Huia.Headless` backend |

`runtimeConfig.public.huiaHeadless` contains **only**
`{ registerPath, loginPath, logoutPath, sessionPath, loginPage, middlewareExclude }` — the backend
URL and session password never reach the browser.

## Route protection

```vue
<script setup lang="ts">
definePageMeta({ middleware: 'auth' })
</script>
```

Set `huiaHeadless.middleware.global = true` to protect everything, with
`huiaHeadless.middleware.exclude = ['/', '/login']` for the public routes (glob: `**` any depth, `*`
one segment). `loginPage` and the module's own `register`/`login`/`logout`/`refresh`/`session`/etc.
routes are always excluded on top of this list — an unauthenticated visit redirects to `loginPage`
with `?returnTo=<path>`, an ordinary internal Nuxt navigation, not an external redirect.

## `useHuia()`

```ts
const { register, login, logout, user, loggedIn } = useHuia()

const reg = await register({ email, password })       // does not sign in
if (reg.ok) {
  const result = await login({ email, password })     // { ok, user? } or { ok: false, error }
}

logout()   // purely local: clears the session cookie + stored tokens, no server round-trip
```

`login()` also accepts `twoFactorCode`/`twoFactorRecoveryCode` for accounts with 2FA enabled.
