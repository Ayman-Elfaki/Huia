export default defineNuxtConfig({
  modules: ['../src/module'],
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },

  huiaHeadless: {
    baseUrl: process.env.NUXT_HUIA_HEADLESS_BASE_URL ?? 'https://localhost:5340',
    session: {
      password: process.env.NUXT_HUIA_HEADLESS_SESSION_PASSWORD ?? 'dev-only-session-password-change-me-1234567890',
    },
  },

  nitro: {
    devStorage: {
      'huia-headless-auth': { driver: 'fs', base: '.data/huia-headless-auth' },
    },
  },
})
