import type { HuiaOidcConfig } from 'next-huia-oidc'

export const huiaConfig: HuiaOidcConfig = {
  baseUrl: process.env.HUIA_BASE_URL ?? process.env.NUXT_PUBLIC_HUIA_BASE_URL ?? 'https://localhost:5310',
  tenant: 'todo',
  clientId: process.env.HUIA_CLIENT_ID ?? 'todo-app',
  clientSecret: process.env.HUIA_CLIENT_SECRET ?? 'todo-app-secret',
  scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
  allowedAuthParams: ['ui_locales'],
  par: { enabled: true },
  session: {
    password: process.env.HUIA_SESSION_PASSWORD ?? 'dev-only-todo-session-password-change-me-01234567890',
    userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
  },
  allowInsecureTls: true,
}
