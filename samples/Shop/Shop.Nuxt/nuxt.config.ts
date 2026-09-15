const shopApiUrl = process.env.NUXT_PUBLIC_SHOP_API_URL ?? 'http://localhost:5341'

export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },
  modules: [
    '@nuxt/ui',
    // first-party auth module — referenced from source in the monorepo
    '../../../src/javascript/nuxt/nuxt-huia-headless/src/module',
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

  // /auth/callback exchanges a one-time code and redirects — no SEO value, transient URL — and
  // server-rendering it actively breaks the exchange: the composables it calls need Nuxt's app
  // context one level deeper than SSR's `withAsyncContext` keeps alive, and even a successful
  // server-side call doesn't propagate the backend's Set-Cookie back to the real response, so the
  // session cookie silently never reaches the browser. CSR-only sidesteps both. (`definePageMeta({
  // ssr: false })` on the page itself was not reliably honoured here — this route-rule form is.)
  routeRules: {
    '/auth/callback': { ssr: false },
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
