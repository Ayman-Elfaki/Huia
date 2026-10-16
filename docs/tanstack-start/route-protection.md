# `@huia/tanstack-start-oidc` — Route Protection

TanStack Start applications can enforce authentication at three distinct boundaries: TanStack Router `beforeLoad`, Server Route Handlers, and HTTP Middleware.

## 1. TanStack Router `beforeLoad` Protection

Use `requireHuiaAuth` to guard routes before navigation occurs:

```tsx
// src/routes/dashboard.tsx
import { createFileRoute } from '@tanstack/react-router'
import { requireHuiaAuth } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '@/auth.config'

export const Route = createFileRoute('/dashboard')({
  beforeLoad: async ({ location }) => {
    return await requireHuiaAuth(huiaConfig, {
      returnTo: location.href,
      roles: ['admin', 'manager'], // Optional role requirement
    })
  },
  component: DashboardPage,
})

function DashboardPage() {
  const { session } = Route.useRouteContext()
  return <h1>Welcome back, {session.user?.name}</h1>
}
```

If the user is not authenticated, `requireHuiaAuth` throws a TanStack Router `redirect({ href: '/api/auth/login?returnTo=...' })`. If roles are specified and missing, it throws a redirect to `/forbidden` or 403.

---

## 2. Server Route Handlers (API Routes)

In TanStack Start server route handlers, inspect the session and token directly:

```ts
// src/routes/api/projects.ts
import { createFileRoute } from '@tanstack/react-router'
import { getAccessToken, getHuiaSession } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '@/auth.config'

export const Route = createFileRoute('/api/projects')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const session = await getHuiaSession(huiaConfig, request)
        if (!session.loggedIn) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        const token = await getAccessToken(huiaConfig, request)
        const res = await fetch('http://api.backend.local/projects', {
          headers: { authorization: `Bearer ${token}` },
        })
        const data = await res.json()
        return Response.json(data)
      },
    },
  },
})
```

---

## 3. Server HTTP Middleware

Use `createHuiaAuthMiddleware` to protect API or page paths at the request middleware layer:

```ts
import { createHuiaAuthMiddleware } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '@/auth.config'

export const authMiddleware = createHuiaAuthMiddleware({
  config: huiaConfig,
  protectedRoutes: ['/dashboard', '/settings', /^\/admin(\/.*)?$/],
  loginUrl: '/api/auth/login',
})
```

---

## 4. Client Components

In React components rendered on the client, use `useUserSession`:

```tsx
import { useUserSession } from '@huia/tanstack-start-oidc/client'

export function AdminPanel() {
  const { user, loggedIn, loading, login } = useUserSession()

  if (loading) return <div>Loading...</div>
  if (!loggedIn) {
    return (
      <div>
        <p>Please log in to continue.</p>
        <button onClick={() => login()}>Sign In</button>
      </div>
    )
  }

  return <div>Welcome, {user?.name}</div>
}
```
