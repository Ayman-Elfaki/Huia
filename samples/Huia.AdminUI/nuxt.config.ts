const huiaBaseUrl = process.env.NUXT_PUBLIC_HUIA_BASE_URL ?? 'https://localhost:5310'

export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },
  modules: [
    '@nuxtjs/tailwindcss',
    '@nuxtjs/color-mode',
    'shadcn-nuxt',
    'nuxt-api-party',
    // first-party auth module — referenced from source in the monorepo
    '../../src/nuxt/src/module',
  ],
  css: ['~/assets/css/main.css'],

  runtimeConfig: {
    huiaBaseUrl,
    public: {
      appName: 'Huia Admin',
    },
  },

  app: {
    head: {
      title: 'Huia Admin',
      htmlAttrs: { lang: 'en' },
    },
  },

  colorMode: {
    classSuffix: '',
    preference: 'dark',
    fallback: 'dark',
  },

  shadcn: {
    prefix: '',
    componentDir: './app/components/ui',
  },

  // Server-side session/token store. In the Aspire stack this is Redis (shared across instances,
  // survives an app restart); a bare `npm run dev` falls back to Nitro's in-memory default.
  nitro: process.env.NUXT_REDIS_URL
    ? { storage: { 'huia-auth': { driver: 'redis', url: process.env.NUXT_REDIS_URL } } }
    : {},

  // Server-side proxy to the master-tenant APIs; the bearer token is added by
  // server/plugins/api-party-auth.ts and never reaches the browser.
  apiParty: {
    endpoints: {
      huia: { url: `${huiaBaseUrl}/master` },
    },
  },

  huiaAuth: {
    huia: { baseUrl: huiaBaseUrl, tenant: 'master' },
    clientId: process.env.NUXT_HUIA_AUTH_CLIENT_ID ?? 'huia-admin-ui',
    clientSecret: process.env.NUXT_HUIA_AUTH_CLIENT_SECRET ?? 'huia-admin-ui-secret',
    scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
    par: { enabled: true, required: true },
    session: {
      password: process.env.NUXT_HUIA_AUTH_SESSION_PASSWORD
        ?? 'dev-only-admin-session-password-change-me-0123456789',
    },
  },
})
