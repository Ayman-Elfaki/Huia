# `@huia/tanstack-start-oidc` — Configuration & Usage

## 1. Installation

```bash
npm install @huia/tanstack-start-oidc huia-auth-core
```

## 2. Defining Configuration

Create an authentication configuration file (e.g., `src/auth.config.ts`):

```ts
import type { HuiaOidcConfig } from '@huia/tanstack-start-oidc'

export const huiaConfig: HuiaOidcConfig = {
  issuer: process.env.HUIA_BASE_URL || 'https://localhost:5310',
  clientId: process.env.HUIA_CLIENT_ID || 'todo-tanstack',
  clientSecret: process.env.HUIA_CLIENT_SECRET || 'todo-tanstack-secret',
  appUrl: process.env.APP_URL || 'https://localhost:3070',
  scopes: ['openid', 'profile', 'email', 'offline_access', 'todos:read', 'todos:write'],
  session: {
    password: process.env.HUIA_SESSION_PASSWORD || 'at-least-32-characters-long-secret-key-1234',
    maxAge: 86400 * 7,
  },
}
```

## 3. Configuring Vite Plugin

In `vite.config.ts`, include `huiaOidc()`:

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { huiaOidc } from '@huia/tanstack-start-oidc/vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  plugins: [
    huiaOidc({
      routeFile: 'src/routes/api/auth/$.ts',
      authConfigImportPath: '@/auth.config',
    }),
    tanstackStart(),
    react(),
  ],
})
```

The plugin automatically verifies that `src/routes/api/auth/$.ts` exists on build or dev startup. If missing, it scaffolds it automatically.

## 4. Catch-all Server Route Handler

In `src/routes/api/auth/$.ts`:

```ts
import { createFileRoute } from '@tanstack/react-router'
import { createHuiaOidcHandler } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '@/auth.config'

const handler = createHuiaOidcHandler(huiaConfig)

export const Route = createFileRoute('/api/auth/$')({
  server: {
    handlers: {
      GET: ({ request }) => handler(request),
      POST: ({ request }) => handler(request),
    },
  },
})
```

This mounts the standard authentication endpoints:
- `GET /api/auth/login`: Initiates PAR + PKCE redirect flow to Huia.
- `GET /api/auth/callback`: Handles the authorization code response and sets the sealed session cookie.
- `GET /api/auth/logout`: Clears the local session and initiates RP logout at the IdP.
- `GET /api/auth/session`: Returns `{ loggedIn, user, expiresAt }` safely without tokens.

## 5. Root Layout & Client Provider

In `src/routes/__root.tsx`:

```tsx
import { createRootRoute, Outlet, HeadContent, Scripts } from '@tanstack/react-router'
import { HuiaOidcProvider } from '@huia/tanstack-start-oidc/client'
import { Header } from '../components/header'
import '../styles.css'

export const Route = createRootRoute({
  component: RootComponent,
})

function RootComponent() {
  return (
    <html lang="en">
      <head>
        <HeadContent />
      </head>
      <body>
        <HuiaOidcProvider>
          <Header />
          <main>
            <Outlet />
          </main>
        </HuiaOidcProvider>
        <Scripts />
      </body>
    </html>
  )
}
```

## 6. Client Hook Usage

In any client component or route:

```tsx
import { useUserSession } from '@huia/tanstack-start-oidc/client'

export function UserProfile() {
  const { user, loggedIn, loading, login, clear } = useUserSession()

  if (loading) return <div>Loading session...</div>

  if (!loggedIn) {
    return <button onClick={() => login()}>Sign In</button>
  }

  return (
    <div>
      <p>Hello, {user?.name || user?.email}!</p>
      <button onClick={() => clear()}>Sign Out</button>
    </div>
  )
}
```

## 7. Server Route Handlers & API Calls

Retrieve the authenticated session or upstream access token inside API handlers:

```ts
import { createFileRoute } from '@tanstack/react-router'
import { getAccessToken, getHuiaSession } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '@/auth.config'

export const Route = createFileRoute('/api/tasks')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const session = await getHuiaSession(huiaConfig, request)
        if (!session.loggedIn) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        const token = await getAccessToken(huiaConfig, request)
        const res = await fetch('http://api.backend.local/tasks', {
          headers: { authorization: `Bearer ${token}` },
        })
        const data = await res.json()
        return Response.json(data)
      },
    },
  },
})
```
