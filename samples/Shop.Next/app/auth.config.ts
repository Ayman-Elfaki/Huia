import { Redis } from 'ioredis'
import { RedisStorageAdapter } from 'huia-auth-core'
import type { HuiaHeadlessConfig } from 'next-huia-headless'

// Next.js's App Router compiles each Route Handler as an independent bundle, so the default in-memory
// storage's module-level Map isn't reliably shared between them — a token record written by
// /api/auth/[...huia]'s login route can be invisible to a completely different route (e.g. /api/cart)
// reading it back moments later, surfacing as spurious 401s despite a valid session cookie. Redis (already
// provisioned for the Nuxt sample apps, see AppHost.cs) gives every route the same shared store.
const storage = process.env.REDIS_URL
  ? new RedisStorageAdapter(new Redis(process.env.REDIS_URL), 'huia:shop-next:')
  : undefined

export const huiaHeadlessConfig: HuiaHeadlessConfig = {
  storage,
  baseUrl: process.env.SHOP_API_URL ?? process.env.NUXT_PUBLIC_SHOP_API_URL ?? 'http://localhost:5341',
  // Next.js's NextRequest.url reflects how the server was started (next dev/next start default to
  // "localhost"), not the request's actual Host header, so the external-login callback URL the handler
  // builds on its own behalf would otherwise silently point at the wrong origin whenever this app is
  // reached under a different hostname — as it is here, via *.dev.localhost under Aspire.
  appUrl: process.env.NEXT_PUBLIC_APP_URL ?? 'http://shop-next.dev.localhost:3060',
  session: {
    password: process.env.HUIA_SESSION_PASSWORD ?? 'dev-only-shop-session-password-change-me-01234567890',
    userClaims: ['sub', 'name', 'email', 'firstName', 'lastName', 'roles', 'phoneNumber'],
  },
  allowInsecureTls: true,
}
