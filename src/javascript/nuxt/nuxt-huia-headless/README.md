# nuxt-huia-headless

**Nuxt 3+** authentication module for [`Huia.Headless`](https://github.com/Ayman-Elfaki/Huia) — a
bearer-token identity API (register, login, refresh, passkeys, passwordless SMS, external providers)
with no OAuth redirect dance for password/phone sign-in. Same dual-layer session principle as
[`nuxt-huia-oidc`](../nuxt-huia-oidc): the browser only ever holds a sealed, chunked session cookie;
access/refresh tokens live server-side in Nitro Storage and are refreshed transparently.

- Requires Nuxt `>=3`, Nitro `>=2.10`, Node `>=20.11`
- No `openid-client` dependency — this talks to Huia.Headless with plain `fetch`, since there is no
  authorization-code redirect or discovery document to negotiate.
- Docs: <https://github.com/Ayman-Elfaki/Huia/tree/main/docs/nuxt-headless>

## Install

```bash
npm install nuxt-huia-headless
```

```ts
// nuxt.config.ts
export default defineNuxtConfig({
  modules: ['nuxt-huia-headless'],
  huiaHeadless: {
    baseUrl: 'https://api.example.com',
    // session.password via NUXT_HUIA_HEADLESS_SESSION_PASSWORD (>= 32 chars)
  },
})
```

## Usage

```vue
<!-- app/pages/login.vue — the app owns this page; Huia.Headless has no hosted login UI -->
<script setup lang="ts">
const { login } = useHuia()
const result = await login({ email, password })
</script>
```

```vue
<script setup lang="ts">
const { user, loggedIn, logout } = useHuia()
</script>
```

Passwordless SMS is a two- or three-step flow — `verifyPhoneLogin` returns the bearer session
directly for a returning, complete-profile account, or `{ requiresProfile: true, flowId }` for a
first-time number that still needs a name:

```vue
<script setup lang="ts">
const { startPhoneLogin, verifyPhoneLogin, completePhoneProfile } = useHuia()

const { flowId } = await startPhoneLogin({ phoneNumber: '+12025550123' })
const result = await verifyPhoneLogin({ flowId, code })
if (result.requiresProfile) {
  await completePhoneProfile({ flowId: result.flowId, firstName, lastName })
}
</script>
```

External login (Google/GitHub/Microsoft/generic OIDC) is a real browser redirect through the
provider, so it starts as a plain link, not a `fetch` call — `externalLoginHref` builds it:

```vue
<!-- app/pages/login.vue -->
<a :href="useHuia().externalLoginHref('google', '/dashboard')">Sign in with Google</a>
```

```vue
<!-- app/pages/auth/callback.vue — the page huiaHeadless.externalCallbackPage points at (default /auth/callback) -->
<script setup lang="ts">
const { code, returnTo } = useRoute().query
const { exchangeExternalCode, completeExternalProfile } = useHuia()

const result = await exchangeExternalCode(code as string)
if (!result.requiresProfile) await navigateTo((returnTo as string) ?? '/')
// else render a small first/last name form, then call completeExternalProfile({ code: result.flowId, firstName, lastName })
</script>
```

```ts
// server/api/me.get.ts
export default defineEventHandler(async (event) => {
  const { user } = await requireUserSession(event)   // 401 if anonymous
  return { id: user.sub, email: user.email, roles: user.roles }
})
```

Protect a page with the built-in `auth` middleware (`definePageMeta({ middleware: 'auth' })`, or
`huiaHeadless.middleware.global = true` for every page); an unauthenticated visit redirects to
`huiaHeadless.loginPage` (default `/login`) rather than an external URL — there is nothing external
to redirect to. `huiaHeadless.middleware.exclude` (glob patterns, `**`/`*` supported) opts specific
pages out; the module's own auth routes and `loginPage` are always excluded regardless.

`logout()` is purely local — it clears the session cookie and the stored token record. Huia.Headless's
bearer tokens are the framework's stock (non-revocable) format, so unlike `nuxt-huia-oidc` there is no
upstream session to end and no redirect.

## Develop

```bash
npm install
npm run dev:prepare
npm test            # Vitest unit + integration (mocked Huia.Headless backend)
npm run dev          # playground on http://localhost:3000
```

`.npmrc` pins `legacy-peer-deps=true` (an npm 10 arborist crash on the nuxt peer graph).
