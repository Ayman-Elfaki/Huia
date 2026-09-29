# Migrating from NextAuth / Auth.js to `next-huia-oidc`

This guide explains how to migrate existing Next.js applications using `next-auth` (Auth.js) to `next-huia-oidc`.

## Conceptual Differences

| Feature | NextAuth / Auth.js | `next-huia-oidc` |
|---|---|---|
| **Architecture** | Broad multi-provider framework | First-party OpenID Connect & OAuth 2.0 client for Huia IdP |
| **Token Storage** | Often in JWT cookies or DB sessions | Dual-layer: encrypted session cookie + secure server token store |
| **Pushed Authorization (PAR)** | Manual or unsupported | Built-in RFC 9126 PAR support via `huia-auth-core` |
| **Multi-Tenancy** | Requires custom callbacks and multi-tenant URL rewriting | Native tenant path support (`/{tenant}/...`) |
| **Refresh Tokens** | Requires complex manual `jwt` callback rotation loops | Transparent automatic background refresh with mutex locking |

---

## Step-by-Step Migration

### 1. Remove Dependencies

Uninstall `next-auth`:

```bash
npm uninstall next-auth
npm install next-huia-oidc huia-auth-core
```

### 2. Replace the Catch-all Route Handler

In NextAuth, routes were handled under `app/api/auth/[...nextauth]/route.ts`. In `next-huia-oidc`, use `createHuiaOidcHandler`:

```ts
// app/api/auth/[...huia]/route.ts
import { createHuiaOidcHandler } from 'next-huia-oidc/server'
import { config } from '@/auth.config'

const handler = createHuiaOidcHandler(config)

export {
  handler as GET,
  handler as POST,
}
```

### 3. Migrate Configuration

Replace `authOptions` or `NextAuth(...)` config with `HuiaOidcConfig`:

```ts
// auth.config.ts
import type { HuiaOidcConfig } from 'next-huia-oidc'

export const config: HuiaOidcConfig = {
  issuer: process.env.HUIA_ISSUER ?? 'https://id.example.com/master',
  clientId: process.env.HUIA_CLIENT_ID ?? 'nextjs-app',
  clientSecret: process.env.HUIA_CLIENT_SECRET,
  redirectUri: process.env.HUIA_REDIRECT_URI ?? 'http://localhost:3000/api/auth/callback',
  postLogoutRedirectUri: 'http://localhost:3000',
  scope: 'openid profile email offline_access',
  session: {
    password: process.env.SESSION_SECRET!, // 32+ char secret for AES-GCM
    maxAge: 60 * 60 * 24 * 7, // 7 days
  },
}
```

### 4. Migrate Server Component Calls

**Before (NextAuth):**
```tsx
import { getServerSession } from 'next-auth'
import { authOptions } from '@/app/api/auth/[...nextauth]/route'

export default async function Page() {
  const session = await getServerSession(authOptions)
  return <div>{session?.user?.name}</div>
}
```

**After (`next-huia-oidc`):**
```tsx
import { getUserSession } from 'next-huia-oidc/server'
import { config } from '@/auth.config'

export default async function Page() {
  const session = await getUserSession(config)
  return <div>{session.loggedIn ? session.user.name : 'Anonymous'}</div>
}
```

### 5. Migrate Client Components

Replace `SessionProvider` with `HuiaOidcProvider`:

```tsx
// app/layout.tsx
import { HuiaOidcProvider } from 'next-huia-oidc/client'

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <HuiaOidcProvider sessionEndpoint="/api/auth/session">
          {children}
        </HuiaOidcProvider>
      </body>
    </html>
  )
}
```

Replace `useSession()` with `useUserSession()`:

```tsx
'use client'

import { useUserSession } from 'next-huia-oidc/client'

export function UserNav() {
  const { session, loggedIn, logout } = useUserSession()

  if (!loggedIn) {
    return <a href="/api/auth/login">Log In</a>
  }

  return (
    <div>
      <span>{session.user.name}</span>
      <button onClick={() => logout()}>Log Out</button>
    </div>
  )
}
```
