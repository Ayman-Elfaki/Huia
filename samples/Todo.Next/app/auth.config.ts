import { Redis } from 'ioredis'
import { RedisStorageAdapter } from 'huia-auth-core'
import type { HuiaOidcConfig } from 'next-huia-oidc'

// Next.js's App Router compiles each Route Handler as an independent bundle, so the default in-memory
// storage's module-level Map isn't reliably shared between them — a token record written by
// /api/auth/[...huia]'s login route can be invisible to a completely different route (e.g. /api/todos)
// reading it back moments later, surfacing as spurious 401s despite a valid session cookie. Redis (already
// provisioned for the Nuxt sample apps, see AppHost.cs) gives every route the same shared store.
const storage = process.env.REDIS_URL
  ? new RedisStorageAdapter(new Redis(process.env.REDIS_URL), 'huia:todo-next:')
  : undefined

export const huiaConfig: HuiaOidcConfig = {
  storage,
  baseUrl: process.env.HUIA_BASE_URL ?? process.env.NUXT_PUBLIC_HUIA_BASE_URL ?? 'https://localhost:5310',
  tenant: 'todo',
  clientId: process.env.HUIA_CLIENT_ID ?? 'todo-app',
  clientSecret: process.env.HUIA_CLIENT_SECRET ?? 'todo-app-secret',
  // Next.js's NextRequest.url reflects how the server was started (next dev/next start default to
  // "localhost"), not the request's actual Host header, so the OAuth redirect_uri/post-logout URLs the
  // handler builds on its own behalf would otherwise silently point at the wrong origin whenever this
  // app is reached under a different hostname — as it is here, via *.dev.localhost under Aspire.
  appUrl: process.env.NEXT_PUBLIC_APP_URL ?? 'http://todo-next.dev.localhost:3050',
  scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
  allowedAuthParams: ['ui_locales'],
  par: { enabled: true },
  session: {
    password: process.env.HUIA_SESSION_PASSWORD ?? 'dev-only-todo-session-password-change-me-01234567890',
    userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
  },
  allowInsecureTls: true,
}
