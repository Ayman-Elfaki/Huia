const huiaBaseUrl = process.env.NUXT_PUBLIC_HUIA_BASE_URL ?? 'https://localhost:5310'
const todoApiUrl = process.env.NUXT_PUBLIC_TODO_API_URL ?? 'http://localhost:5330'

export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },
  modules: [
    '@nuxtjs/tailwindcss',
    '@nuxtjs/color-mode',
    '@nuxtjs/i18n',
    'shadcn-nuxt',
    'nuxt-api-party',
    // first-party auth module — referenced from source in the monorepo
    '../../../src/javascript/nuxt/nuxt-huia-oidc/src/module',
  ],
  css: ['~/assets/css/main.css'],

  i18n: {
    strategy: 'no_prefix',
    defaultLocale: 'en',
    locales: [
      { code: 'en', language: 'en', name: 'English', dir: 'ltr', file: 'en.json' },
      { code: 'ar', language: 'ar', name: 'العربية', dir: 'rtl', file: 'ar.json' },
    ],
    detectBrowserLanguage: {
      useCookie: true,
      cookieKey: 'huia_todo_locale',
      redirectOn: 'root',
    },
  },

  runtimeConfig: {
    todoApiUrl,
    huiaBaseUrl,
    public: {
      appName: 'Huia Todo',
      // The identity-provider origin, for links into the account UI (e.g. managing linked providers).
      huiaBaseUrl,
    },
  },

  app: {
    head: {
      title: 'Huia Todo',
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

  // Server-side proxy to the two upstreams; the bearer token is added by
  // server/plugins/api-party-auth.ts and never reaches the browser.
  apiParty: {
    endpoints: {
      huia: { url: `${huiaBaseUrl}/todo` },
      todoApi: { url: todoApiUrl },
    },
  },
  // HTTPS for the dev server when the AppHost supplies the ASP.NET dev certificate. PEM (cert + key) is preferred:
  // Nuxt's certificate parser cannot read the PFX Aspire exports ("Cannot read properties of undefined (reading 'n')").
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

  huia: {
    baseUrl: huiaBaseUrl,
    tenant: 'todo',
    clientId: process.env.NUXT_HUIA_CLIENT_ID ?? 'todo-app',
    clientSecret: process.env.NUXT_HUIA_CLIENT_SECRET ?? 'todo-app-secret',
    scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
    // forward ?ui_locales=<locale> to /connect/authorize so the Huia account UI matches the app locale
    allowedAuthParams: ['ui_locales'],
    par: { enabled: true },
    session: {
      password: process.env.NUXT_HUIA_SESSION_PASSWORD
        ?? 'dev-only-todo-session-password-change-me-01234567890',
      userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
    },
  },
})
