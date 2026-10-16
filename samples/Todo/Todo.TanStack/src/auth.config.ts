import { Redis } from 'ioredis'
import { RedisStorageAdapter } from 'huia-auth-core'
import type { HuiaOidcConfig } from '@huia/tanstack-start-oidc'

const storage = process.env.REDIS_URL
  ? new RedisStorageAdapter(new Redis(process.env.REDIS_URL), 'huia:todo-tanstack:')
  : undefined

export const huiaConfig: HuiaOidcConfig = {
  storage,
  baseUrl: process.env.HUIA_BASE_URL ?? process.env.NUXT_PUBLIC_HUIA_BASE_URL ?? 'https://localhost:5310',
  tenant: 'todo',
  clientId: process.env.HUIA_CLIENT_ID ?? 'todo-tanstack',
  clientSecret: process.env.HUIA_CLIENT_SECRET ?? 'todo-tanstack-secret',
  appUrl: process.env.APP_URL ?? 'https://localhost:3070',
  scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
  allowedAuthParams: ['ui_locales'],
  par: { enabled: true },
  session: {
    password: process.env.HUIA_SESSION_PASSWORD ?? 'dev-only-todo-session-password-change-me-01234567890',
    userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
  },
  allowInsecureTls: true,
}
