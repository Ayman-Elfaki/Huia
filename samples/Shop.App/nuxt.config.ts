const shopApiUrl = process.env.NUXT_PUBLIC_SHOP_API_URL ?? 'http://localhost:5341'

export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },
  modules: [
    '@nuxt/ui',
    // first-party auth module — referenced from source in the monorepo
    '../../src/nuxt/nuxt-huia-headless/src/module',
  ],

  css: ['~/assets/css/main.css'],

  runtimeConfig: {
    shopApiUrl,
    public: {
      appName: 'Huia Shop',
    },
  },

  app: {
    head: {
      title: 'Huia Shop',
    },
  },

  huiaHeadless: {
    baseUrl: shopApiUrl,
    session: {
      password: process.env.NUXT_HUIA_HEADLESS_SESSION_PASSWORD
        ?? 'dev-only-shop-session-password-change-me-0123456789',
    },
  },

  nitro: {
    devStorage: {
      'huia-headless-auth': { driver: 'fs', base: '.data/huia-headless-auth' },
    },
  },
})
