import { Redis } from 'ioredis'
import { RedisStorageAdapter } from 'huia-auth-core'
import type { HuiaHeadlessConfig } from '@huia/tanstack-start-headless'

const storage = process.env.REDIS_URL
  ? new RedisStorageAdapter(new Redis(process.env.REDIS_URL), 'huia:shop-tanstack:')
  : undefined

export const huiaHeadlessConfig: HuiaHeadlessConfig = {
  storage,
  baseUrl: process.env.SHOP_API_URL ?? process.env.NUXT_PUBLIC_SHOP_API_URL ?? 'http://localhost:5341',
  appUrl: process.env.APP_URL ?? process.env.TANSTACK_PUBLIC_APP_URL ?? 'https://localhost:3080',
  session: {
    password: process.env.HUIA_SESSION_PASSWORD ?? 'dev-only-shop-session-password-change-me-01234567890',
    userClaims: ['sub', 'name', 'email', 'firstName', 'lastName', 'roles', 'phoneNumber'],
  },
  allowInsecureTls: true,
}
