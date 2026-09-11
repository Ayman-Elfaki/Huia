# nuxt-huia-headless

**Nuxt 4** bearer-token authentication module for the [Huia](https://github.com/Ayman-Elfaki/Huia) identity provider in headless mode.

Direct API authentication with transparent server-side session and refresh-token rotation, keeping **every token on the server**.

- Native support for Email/Password, Two-Factor Authentication, and Passwordless Phone SMS login.
- Direct interaction with Huia's `/{tenant}/identity/*` minimal APIs.
- Rotated refresh tokens with reuse detection.
- Dual-layer server session: sealed encrypted cookie carrying session id + display claims, token pairs securely stored in Nitro Storage.

## Install

```bash
npm install nuxt-huia-headless
```

```ts
// nuxt.config.ts
export default defineNuxtConfig({
  modules: ['nuxt-huia-headless'],
  huiaHeadless: {
    huia: { baseUrl: 'https://id.example.com', tenant: 'shop' },
    // session.password via NUXT_HUIA_HEADLESS_SESSION_PASSWORD (>= 32 chars)
  },
})
```

## Usage

```vue
<script setup lang="ts">
const { user, loggedIn } = useUserSession()
const { login, register, logout, phoneLoginStart, phoneLoginVerify } = useAuth()
</script>
```
