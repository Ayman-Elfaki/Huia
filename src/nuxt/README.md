# huia-nuxt

First-party **Nuxt 4** authentication module for the [Huia](https://github.com/Ayman-Elfaki/Huia)
identity provider. OIDC Authorization Code flow with PKCE, RFC 9126 Pushed Authorization Requests,
transparent server-side token refresh, and a dual-layer session that keeps **every token on the
server**.

- Built on [`openid-client`](https://github.com/panva/openid-client) v6 (ESM, Web Crypto)
- Requires Nuxt `>=4`, Nitro `>=2.10`, Node `>=20.11`
- Full spec: [`SPEC.md`](./SPEC.md) · docs: <https://github.com/Ayman-Elfaki/Huia/tree/main/docs/nuxt>

## Install

```bash
npm install huia-nuxt
```

```ts
// nuxt.config.ts
export default defineNuxtConfig({
  modules: ['huia-nuxt'],
  huiaAuth: {
    huia: { baseUrl: 'https://id.example.com', tenant: 'acme' },
    clientId: 'acme-web',
    // clientSecret via NUXT_HUIA_AUTH_CLIENT_SECRET
    scopes: ['openid', 'profile', 'email', 'offline_access'],
    par: { enabled: true },
    // session.password via NUXT_HUIA_AUTH_SESSION_PASSWORD (>= 32 chars)
  },
})
```

## Usage

```vue
<script setup lang="ts">
const { user, loggedIn } = useUserSession()
const { login, logout } = useAuth()
</script>
```

```ts
// server/api/me.get.ts
export default defineEventHandler(async (event) => {
  const { user } = await requireUserSession(event)   // 401 if anonymous
  return { id: user.sub, name: user.name }
})
```

The browser only ever holds a sealed, chunked `__Host-huia_sess` cookie carrying the session id and a
whitelisted claim subset. Access / refresh / id tokens live in Nitro Storage, keyed by that id, and
are refreshed transparently. See [`SPEC.md`](./SPEC.md) for the architecture, the cookie-chunking and
soft-lock algorithms, the security table, and the test strategy.

## Develop

```bash
npm install
npm run dev:prepare
npm test            # Vitest unit + integration (mocked OP)
npm run dev         # playground on http://localhost:3000
```

`.npmrc` pins `legacy-peer-deps=true` (an npm 10 arborist crash on the nuxt peer graph).
