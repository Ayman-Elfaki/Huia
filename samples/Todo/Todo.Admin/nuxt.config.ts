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
    '../../../src/javascript/nuxt/nuxt-huia-oidc/src/module',
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

  // HTTPS for the dev server when the AppHost supplies the ASP.NET dev certificate.
  devServer: {
    https: process.env.TLS_CONFIG_CERT && process.env.TLS_CONFIG_KEY
      ? {
        cert: process.env.TLS_CONFIG_CERT,
        key: process.env.TLS_CONFIG_KEY,
        passphrase: process.env.TLS_CONFIG_PASSWORD
      }
      : process.env.TLS_CONFIG_PFX
        ? {
          pfx: process.env.TLS_CONFIG_PFX,
          passphrase: process.env.TLS_CONFIG_PASSWORD
        }
        : undefined
  },

  // Server-side proxy to the master-tenant APIs; the bearer token is added by
  // server/plugins/api-party-auth.ts and never reaches the browser.
  apiParty: {
    endpoints: {
      huia: { url: `${huiaBaseUrl}/master` },
    },
  },

  huia: {
    baseUrl: huiaBaseUrl,
    tenant: 'master',
    clientId: process.env.NUXT_HUIA_CLIENT_ID ?? 'todo-admin',
    clientSecret: process.env.NUXT_HUIA_CLIENT_SECRET ?? 'todo-admin-secret',
    scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
    par: { enabled: true, required: true },
    session: {
      password: process.env.NUXT_HUIA_SESSION_PASSWORD
        ?? 'dev-only-admin-session-password-change-me-0123456789',
    },
  },
})
