# Client Session Configuration & Redis Integration Reference

Guidance for configuring session persistence, cookie settings, and multi-instance caching in client applications.

---

## 1. Multi-Instance Session Storage with Redis

Next.js Route Handlers and Nuxt Nitro workers can run in separate processes or serverless functions. To ensure tokens and locks are shared across all instances, configure Redis storage:

### Next.js with `ioredis`
```ts
import { Redis } from 'ioredis'
import { RedisStorageAdapter } from 'huia-auth-core'
import type { HuiaOidcConfig } from 'next-huia-oidc'

const redis = new Redis(process.env.REDIS_URL!, {
  maxRetriesPerRequest: 3,
  enableReadyCheck: false,
})

export const huiaConfig: HuiaOidcConfig = {
  // ...
  storage: new RedisStorageAdapter(redis, 'huia:session:'),
}
```

### Nuxt 4 with Nitro Storage
In `nuxt.config.ts`:
```ts
export default defineNuxtConfig({
  modules: ['nuxt-huia-oidc'],
  nitro: {
    storage: {
      'huia-auth': {
        driver: 'redis',
        url: process.env.REDIS_URL,
      },
    },
  },
})
```

---

## 2. Session Cookie Customization

You can customize cookie behavior under `session.cookie`:

```ts
session: {
  password: process.env.HUIA_SESSION_PASSWORD!,
  cookie: {
    name: 'custom_session', // Base cookie name (default: huia_session)
    secure: process.env.NODE_ENV === 'production', // HTTPS only in production
    sameSite: 'lax', // 'lax' | 'strict' | 'none'
    maxAge: 60 * 60 * 24 * 7, // 7 days in seconds
    path: '/',
  }
}
```

---

## 3. Automatic Token Refresh Behavior

- Access tokens are automatically refreshed in the background when calling `getAccessToken()` or when making authenticated requests through the client.
- The refresh token is securely stored inside the encrypted session cookie on the server and is never exposed to the client-side JavaScript.
- If the refresh token has expired or has been revoked at the identity provider, `getUserSession()` returns `null` and the user will need to sign in again.
