# `@huia/tanstack-start-headless` — Route Protection

Route protection in `@huia/tanstack-start-headless` can be implemented at the TanStack Router `beforeLoad` level, inside Server Route Handlers, or within client components.

## 1. TanStack Router `beforeLoad` Protection

Use `requireHuiaHeadlessAuth` to ensure routes are only accessible to authenticated users:

```tsx
// src/routes/cart.tsx
import { createFileRoute, redirect } from '@tanstack/react-router'
import { requireHuiaHeadlessAuth } from '@huia/tanstack-start-headless/server'
import { huiaHeadlessConfig } from '@/auth.config'

export const Route = createFileRoute('/cart')({
  beforeLoad: async ({ location }) => {
    return await requireHuiaHeadlessAuth(huiaHeadlessConfig, {
      returnTo: location.href,
      loginUrl: '/login',
    })
  },
  component: CartPage,
})
```

## 2. Server Route Handlers

Protect custom API endpoints:

```ts
import { createFileRoute } from '@tanstack/react-router'
import { getAccessToken } from '@huia/tanstack-start-headless/server'
import { huiaHeadlessConfig } from '@/auth.config'

export const Route = createFileRoute('/api/orders')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const token = await getAccessToken(huiaHeadlessConfig, request)
        if (!token) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        const res = await fetch(`${huiaHeadlessConfig.baseUrl}/orders`, {
          headers: { authorization: `Bearer ${token}` },
        })
        const data = await res.json()
        return Response.json(data)
      },
    },
  },
})
```
