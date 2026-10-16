# `@huia/tanstack-start-headless` — Configuration & Usage

## 1. Installation

```bash
npm install @huia/tanstack-start-headless huia-auth-core
```

## 2. Defining Configuration

Create an authentication configuration file (e.g., `src/auth.config.ts`):

```ts
import type { HuiaHeadlessConfig } from '@huia/tanstack-start-headless'

export const huiaHeadlessConfig: HuiaHeadlessConfig = {
  baseUrl: process.env.SHOP_API_URL || 'http://localhost:5341',
  appUrl: process.env.APP_URL || 'https://localhost:3080',
  session: {
    password: process.env.HUIA_SESSION_PASSWORD || 'at-least-32-characters-long-secret-key-1234',
    userClaims: ['sub', 'name', 'email', 'firstName', 'lastName', 'roles', 'phoneNumber'],
  },
  allowInsecureTls: true, // for local dev with self-signed certs
}
```

## 3. Configuring Vite Plugin

In `vite.config.ts`:

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { huiaHeadless } from '@huia/tanstack-start-headless/vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  plugins: [
    huiaHeadless({
      routeFile: 'src/routes/api/auth/$.ts',
      authConfigImportPath: '@/auth.config',
    }),
    tanstackStart(),
    react(),
  ],
})
```

## 4. Catch-all Server Route Handler

In `src/routes/api/auth/$.ts`:

```ts
import { createFileRoute } from '@tanstack/react-router'
import { createHuiaHeadlessHandler } from '@huia/tanstack-start-headless/server'
import { huiaHeadlessConfig } from '@/auth.config'

const handler = createHuiaHeadlessHandler(huiaHeadlessConfig)

export const Route = createFileRoute('/api/auth/$')({
  server: {
    handlers: {
      GET: ({ request }) => handler(request),
      POST: ({ request }) => handler(request),
      PUT: ({ request }) => handler(request),
      PATCH: ({ request }) => handler(request),
      DELETE: ({ request }) => handler(request),
    },
  },
})
```

Mounted endpoints include:
- `POST /api/auth/login`: Email and password login.
- `POST /api/auth/register`: User self-registration.
- `POST /api/auth/phone/start`: Initiates phone SMS code dispatch.
- `POST /api/auth/phone/verify`: Verifies SMS OTP code.
- `POST /api/auth/phone/profile`: Completes first-time phone user profile.
- `GET /api/auth/external/login`: Initiates external IdP redirect.
- `POST /api/auth/external/exchange`: Exchanges external code for session.
- `POST /api/auth/logout`: Clears session cookie and drops tokens.
- `GET /api/auth/session`: Inspects session safely.

## 5. Root Layout & Client Provider

In `src/routes/__root.tsx`:

```tsx
import { createRootRoute, Outlet, HeadContent, Scripts } from '@tanstack/react-router'
import { HuiaHeadlessProvider } from '@huia/tanstack-start-headless/client'

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
        <HuiaHeadlessProvider>
          <Outlet />
        </HuiaHeadlessProvider>
        <Scripts />
      </body>
    </html>
  )
}
```

## 6. Client Hook Usage

In any client route:

```tsx
import { useHuia, useUserSession } from '@huia/tanstack-start-headless/client'

export function LoginBox() {
  const { login, register, startPhoneLogin } = useHuia()
  const { loggedIn, user, clear } = useUserSession()

  if (loggedIn) {
    return (
      <div>
        <p>Signed in as {user?.email}</p>
        <button onClick={() => clear()}>Sign Out</button>
      </div>
    )
  }

  return (
    <form onSubmit={async (e) => {
      e.preventDefault()
      await login({ email: 'user@example.com', password: 'Password1!' })
    }}>
      <button type="submit">Sign In</button>
    </form>
  )
}
```
