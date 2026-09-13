import type { HuiaHeadlessConfig } from 'next-huia-headless'

export const huiaHeadlessConfig: HuiaHeadlessConfig = {
  baseUrl: process.env.SHOP_API_URL ?? process.env.NUXT_PUBLIC_SHOP_API_URL ?? 'http://localhost:5341',
  session: {
    password: process.env.HUIA_SESSION_PASSWORD ?? 'dev-only-shop-session-password-change-me-01234567890',
    userClaims: ['sub', 'name', 'email', 'firstName', 'lastName', 'roles', 'phoneNumber'],
  },
  allowInsecureTls: true,
}
