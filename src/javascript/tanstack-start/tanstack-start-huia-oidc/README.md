# @huia/tanstack-start-oidc

TanStack Start authentication package for the [Huia.OpenId](https://github.com/Ayman-Elfaki/Huia) identity provider.

Provides OIDC / OAuth2 authentication with PKCE, RFC 9126 Pushed Authorization Requests (PAR), chunked and sealed session cookies, token refresh, and server functions.

## Installation

```bash
npm install @huia/tanstack-start-oidc huia-auth-core
```

## Quick Start

### 1. Configure Auth (`app/auth.config.ts`)

```ts
import type { HuiaOidcConfig } from '@huia/tanstack-start-oidc'

export const huiaConfig: HuiaOidcConfig = {
  baseUrl: process.env.HUIA_BASE_URL ?? 'https://id.example.com',
  tenant: 'acme',
  clientId: process.env.HUIA_CLIENT_ID ?? 'acme-web',
  clientSecret: process.env.HUIA_CLIENT_SECRET,
  appUrl: process.env.APP_URL ?? 'https://app.example.com',
  session: {
    password: process.env.HUIA_SESSION_PASSWORD!,
  },
}
```

### 2. Register Vite Plugin (`vite.config.ts`)

```ts
import { defineConfig } from 'vite'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { huiaOidc } from '@huia/tanstack-start-oidc/vite'

export default defineConfig({
  plugins: [
    huiaOidc(),
    tanstackStart(),
  ],
})
```

The plugin automatically scaffolds `src/routes/api/auth/$.ts` if it doesn't already exist.

### 3. Client Session & Components

Wrap your root route with `<HuiaOidcProvider>`:

```tsx
import { HuiaOidcProvider, useUserSession } from '@huia/tanstack-start-oidc/client'

export function Navigation() {
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

### 4. Protecting Routes

```tsx
import { createFileRoute } from '@tanstack/react-router'
import { requireHuiaAuth } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '@/auth.config'

export const Route = createFileRoute('/dashboard')({
  beforeLoad: async ({ location }) => {
    return await requireHuiaAuth({ config: huiaConfig, returnTo: location.href })
  },
  component: DashboardComponent,
})
```

## License

MIT
