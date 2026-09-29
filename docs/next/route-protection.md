# `next-huia-oidc` — Route Protection

Next.js applications can enforce authentication at three distinct boundaries: Middleware, Server Components, and API Route Handlers.

## 1. Edge Middleware Protection

Use `withHuiaOidcAuth` in `middleware.ts` to redirect anonymous users before page rendering or data fetching begins:

```ts
// middleware.ts
import { withHuiaOidcAuth } from 'next-huia-oidc/middleware'
import { config } from './auth.config'

export default withHuiaOidcAuth({
  config,
  protectedRoutes: [
    '/dashboard',
    '/settings',
    /^\/admin(\/.*)?$/,
  ],
  loginUrl: '/api/auth/login', // default: config.routes.login
})

export const config = {
  matcher: ['/((?!_next/static|_next/image|favicon.ico).*)'],
}
```

When an unauthenticated request matches `protectedRoutes`, the middleware automatically redirects to the login URL with a `returnTo` query parameter preserving the original destination.

---

## 2. Server Components Protection

For fine-grained control and role checks inside React Server Components:

```tsx
// app/admin/page.tsx
import { redirect } from 'next/navigation'
import { requireUserSession } from 'next-huia-oidc/server'
import { config } from '@/auth.config'

export default async function AdminPage() {
  // Throws or redirects if no valid session
  const session = await requireUserSession(config).catch(() => {
    redirect('/api/auth/login?returnTo=/admin')
  })

  // Role validation
  if (!session.user.roles?.includes('admin')) {
    return <h1>Access Denied</h1>
  }

  return (
    <main>
      <h1>Admin Dashboard</h1>
      <p>Signed in as: {session.user.email}</p>
    </main>
  )
}
```

---

## 3. Route Handlers (API Routes)

In Next.js App Router Route Handlers, protect backend API endpoints and forward tokens:

```ts
// app/api/protected-data/route.ts
import { NextResponse } from 'next/server'
import { requireUserSession, getAccessToken } from 'next-huia-oidc/server'
import { config } from '@/auth.config'

export async function GET() {
  try {
    const session = await requireUserSession(config)
    const token = await getAccessToken(config)

    // Forward the token to an upstream backend API
    const res = await fetch('https://api.example.com/data', {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })

    const data = await res.json()
    return NextResponse.json({ user: session.user, data })
  } catch {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 })
  }
}
```

---

## 4. Client Components Protection

Wrap your root layout in `HuiaOidcProvider`, and use the `useUserSession` hook in client components:

```tsx
'use client'

import { useUserSession } from 'next-huia-oidc/client'

export function UserStatus() {
  const { session, loggedIn, logout } = useUserSession()

  if (!loggedIn) {
    return <a href="/api/auth/login">Sign In</a>
  }

  return (
    <div>
      <span>Hello, {session?.user?.name}</span>
      <button onClick={() => logout()}>Sign Out</button>
    </div>
  )
}
```
