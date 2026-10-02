---
name: huia-frontend-auth
description: >-
  Use this skill when integrating Huia authentication into Next.js or Nuxt applications,
  configuring next-huia-oidc, next-huia-headless, nuxt-huia-oidc, or nuxt-huia-headless,
  setting up session encryption, route handlers, hooks, or calling protected APIs.
---

# Consuming Huia in Next.js and Nuxt Applications

This guide provides step-by-step instructions for integrating Huia authentication into client web applications.

- **Next.js**: Use `next-huia-oidc` (for `Huia.OpenId`) or `next-huia-headless` (for `Huia.Headless`).
- **Nuxt 4**: Use `nuxt-huia-oidc` or `nuxt-huia-headless`.
- **Shared Primitives**: `huia-auth-core` handles encrypted cookies, refresh mutex, and Redis adapters.

For complete option schemas, see [Packages & Options Reference](./references/packages.md).  
For Redis setup and multi-node session storage, see [Session & Storage Reference](./references/session-management.md).

---

## 1. Next.js 14/15 Integration (`next-huia-oidc`)

### Step 1: Install Dependencies
```bash
npm install next-huia-oidc huia-auth-core
# Optional if using Redis for multi-instance session caching:
npm install ioredis
```

### Step 2: Define Authentication Configuration (`app/auth.config.ts`)
```ts
import type { HuiaOidcConfig } from 'next-huia-oidc'
import { RedisStorageAdapter } from 'huia-auth-core'
import { Redis } from 'ioredis'

const storage = process.env.REDIS_URL
  ? new RedisStorageAdapter(new Redis(process.env.REDIS_URL), 'huia:session:')
  : undefined // Falls back to default in-memory storage

export const huiaConfig: HuiaOidcConfig = {
  baseUrl: process.env.HUIA_BASE_URL ?? 'https://id.example.com',
  tenant: 'acme',
  clientId: process.env.HUIA_CLIENT_ID ?? 'acme-web',
  clientSecret: process.env.HUIA_CLIENT_SECRET, // Required for confidential clients
  appUrl: process.env.NEXT_PUBLIC_APP_URL ?? 'https://app.example.com',
  scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
  par: { enabled: true }, // RFC 9126 Pushed Authorization Requests
  session: {
    password: process.env.HUIA_SESSION_PASSWORD!, // 32+ char secret for iron-webcrypto
    userClaims: ['sub', 'name', 'email', 'roles'],
  },
  storage,
}
```

### Step 3: Create the Route Handler (`app/api/auth/[...huia]/route.ts`)
```ts
import { createHuiaOidcHandler } from 'next-huia-oidc/server'
import { huiaConfig } from '@/app/auth.config'

const handler = createHuiaOidcHandler(huiaConfig)

export const GET = handler
export const POST = handler
```
This automatically handles:
- `/api/auth/login`: Initiates OIDC authorization flow (with PKCE + PAR).
- `/api/auth/callback`: Handles identity provider redirect, code exchange, and sets encrypted session cookie.
- `/api/auth/logout`: Clears session cookie and performs identity provider logout.
- `/api/auth/session`: Returns current user session state.

### Step 4: Client-Side Hooks & Components
In React Client Components (`'use client'`):
```tsx
'use client'
import { useUserSession } from 'next-huia-oidc/client'

export function NavigationBar() {
  const { user, loggedIn, login, clear } = useUserSession()

  if (!loggedIn) {
    return <button onClick={() => login()}>Sign In</button>
  }

  return (
    <div>
      <span>Signed in as: {user?.name ?? user?.email}</span>
      <button onClick={() => clear()}>Sign Out</button>
    </div>
  )
}
```

### Step 5: Server Components & Route Handlers
In React Server Components or API routes:
```ts
import { getUserSession } from 'next-huia-oidc/server'
import { huiaConfig } from '@/app/auth.config'

export default async function DashboardPage() {
  const session = await getUserSession(huiaConfig)
  if (!session) {
    return <div>Please sign in to view this page.</div>
  }

  return <h1>Welcome back, {session.user.name}!</h1>
}
```

---

## 2. Nuxt 4 Integration (`nuxt-huia-oidc`)

### Step 1: Install Dependencies
```bash
npm install nuxt-huia-oidc huia-auth-core
```

### Step 2: Register Module in `nuxt.config.ts`
```ts
export default defineNuxtConfig({
  modules: ['nuxt-huia-oidc'],

  huia: {
    baseUrl: process.env.HUIA_BASE_URL ?? 'https://id.example.com',
    tenant: 'acme',
    clientId: process.env.HUIA_CLIENT_ID ?? 'acme-web',
    clientSecret: process.env.HUIA_CLIENT_SECRET,
    scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
    par: { enabled: true },
    session: {
      password: process.env.HUIA_SESSION_PASSWORD!,
      userClaims: ['sub', 'name', 'email', 'roles'],
    },
  },

  // Optional: Redis for Nitro server session store
  nitro: process.env.REDIS_URL
    ? { storage: { 'huia-auth': { driver: 'redis', url: process.env.REDIS_URL } } }
    : {},
})
```

### Step 3: Use Auto-Imported Composables in Vue Components
```vue
<script setup lang="ts">
const { user, loggedIn } = useUserSession()
const { login, logout } = useHuia()
</script>

<template>
  <header>
    <div v-if="loggedIn">
      <span>{{ user?.name || user?.email }}</span>
      <button @click="logout()">Sign Out</button>
    </div>
    <button v-else @click="login()">Sign In</button>
  </header>
</template>
```

---

## 3. Consuming `Huia.Headless` (Pure JSON Auth)

If your backend uses `Huia.Headless` instead of OIDC Razor Pages:

### Next.js (`next-huia-headless`)
```ts
// app/api/auth/[...huia]/route.ts
import { createHuiaHeadlessHandler } from 'next-huia-headless/server'

export const { GET, POST } = createHuiaHeadlessHandler({
  baseUrl: 'https://api.example.com',
  session: { password: process.env.HUIA_SESSION_PASSWORD! }
})
```
Client component usage with `useHuia`:
```tsx
'use client'
import { useHuia } from 'next-huia-headless/client'

export function LoginForm() {
  const { loginWithPassword, startPhoneLogin, verifyPhoneCode } = useHuia()
  // Trigger custom login forms directly via JSON REST calls
}
```
