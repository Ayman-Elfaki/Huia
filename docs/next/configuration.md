# `next-huia-oidc` — Configuration & Usage

## 1. Installation

```bash
npm install next-huia-oidc huia-auth-core
```

## 2. Defining Configuration

Create an authentication configuration file (e.g., `app/auth.config.ts`):

```ts
import type { HuiaOidcConfig } from 'next-huia-oidc/server'

export const huiaOidcConfig: HuiaOidcConfig = {
  issuer: process.env.HUIA_BASE_URL || 'http://localhost:5325',
  clientId: 'todo-app',
  clientSecret: process.env.HUIA_CLIENT_SECRET, // required if client authentication is needed
  // This app's own canonical origin — used to build the OAuth redirect_uri, plus the post-login,
  // post-logout and error redirects the handler issues on its own behalf. Required whenever the app is
  // reached under a hostname the Next.js server itself doesn't know about (a *.localhost alias, a
  // reverse proxy, a container's service name): NextRequest.url reflects how the server was started
  // (next dev/next start default to "localhost"), never the incoming request's actual Host header, so
  // without this the handler would silently redirect to the wrong origin. Omit it only when the app's
  // configured listen address and its externally-reachable hostname are guaranteed to match.
  appUrl: process.env.NEXT_PUBLIC_APP_URL || 'http://todo-next.dev.localhost:3050',
  scopes: ['openid', 'profile', 'email', 'offline_access', 'todos:read', 'todos:write'],
  session: {
    password: process.env.HUIA_SESSION_PASSWORD || 'at-least-32-characters-long-secret-key-1234',
    maxAge: 86400 * 7,
  },
}
```

## 3. Catch-all Route Handler

Create `app/api/auth/[...huia]/route.ts`:

```ts
import { createHuiaOidcHandler } from 'next-huia-oidc/server'
import { huiaOidcConfig } from '@/app/auth.config'

const handler = createHuiaOidcHandler(huiaOidcConfig)

export const GET = handler
export const POST = handler
```

This mounts the following endpoints:
- `GET /api/auth/login`: Starts the PAR + PKCE redirect flow to Huia.
- `GET /api/auth/callback`: Handles the authorization code response, completes token exchange, and sets the sealed cookie.
- `GET /api/auth/logout`: Clears the local session and initiates RP logout at the IdP.
- `GET /api/auth/session`: Returns `{ loggedIn, user, expiresAt }` safely without tokens.

## 4. Root Layout & Client Provider

In `app/layout.tsx`:

```tsx
import { HuiaOidcProvider } from 'next-huia-oidc/client'

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <HuiaOidcProvider>
          {children}
        </HuiaOidcProvider>
      </body>
    </html>
  )
}
```

## 5. Client Hook Usage

In any Client Component:

```tsx
'use client'

import { useUserSession } from 'next-huia-oidc/client'

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

## 6. Server Components & Route Handlers

Retrieve the user session or upstream access token server-side:

```ts
import { getHuiaSession, getAccessToken } from 'next-huia-oidc/server'
import { huiaOidcConfig } from '@/app/auth.config'

export async function GET() {
  const session = await getHuiaSession(huiaOidcConfig)
  if (!session?.loggedIn) {
    return new Response('Unauthorized', { status: 401 })
  }

  const token = await getAccessToken(huiaOidcConfig)
  const response = await fetch('http://api.backend.local/data', {
    headers: { Authorization: `Bearer ${token}` }
  })
  const data = await response.json()
  return Response.json(data)
}
```
