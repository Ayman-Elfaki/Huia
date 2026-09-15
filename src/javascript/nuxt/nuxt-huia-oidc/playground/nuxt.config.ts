export default defineNuxtConfig({
  modules: ['../src/module'],
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },

  huia: {
    baseUrl: process.env.NUXT_HUIA_BASE_URL ?? 'https://localhost:5310',
    tenant: process.env.NUXT_HUIA_TENANT ?? 'todo',
    clientId: process.env.NUXT_HUIA_CLIENT_ID ?? 'todo-app',
    clientSecret: process.env.NUXT_HUIA_CLIENT_SECRET ?? 'todo-app-secret',
    scopes: ['openid', 'profile', 'email', 'offline_access'],
    par: { enabled: true },
    session: {
      password: process.env.NUXT_HUIA_SESSION_PASSWORD ?? 'dev-only-session-password-change-me-1234567890',
    },
  },

  nitro: {
    devStorage: {
      'huia-auth': { driver: 'fs', base: '.data/huia-auth' },
    },
  },
})
