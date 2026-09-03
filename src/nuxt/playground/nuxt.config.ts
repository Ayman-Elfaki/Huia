export default defineNuxtConfig({
  modules: ['../src/module'],
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },

  huiaAuth: {
    huia: {
      baseUrl: process.env.NUXT_HUIA_AUTH_HUIA_BASE_URL ?? 'https://localhost:5310',
      tenant: process.env.NUXT_HUIA_AUTH_HUIA_TENANT ?? 'todo',
    },
    clientId: process.env.NUXT_HUIA_AUTH_CLIENT_ID ?? 'todo-app',
    clientSecret: process.env.NUXT_HUIA_AUTH_CLIENT_SECRET ?? 'todo-app-secret',
    scopes: ['openid', 'profile', 'email', 'offline_access'],
    par: { enabled: true },
    session: {
      password: process.env.NUXT_HUIA_AUTH_SESSION_PASSWORD ?? 'dev-only-session-password-change-me-1234567890',
    },
    // Dev: Node's undici rejects the ASP.NET Core dev certificate.
    allowInsecureTls: true,
  },

  nitro: {
    devStorage: {
      'huia-auth': { driver: 'fs', base: '.data/huia-auth' },
    },
  },
})
