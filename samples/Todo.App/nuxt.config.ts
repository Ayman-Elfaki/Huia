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
    '../../src/nuxt/src/module',
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

  // Server-side proxy to the two upstreams; the bearer token is added by
  // server/plugins/api-party-auth.ts and never reaches the browser.
  apiParty: {
    endpoints: {
      huia: { url: `${huiaBaseUrl}/todo` },
      todoApi: { url: todoApiUrl },
    },
  },

  huiaAuth: {
    huia: { baseUrl: huiaBaseUrl, tenant: 'todo' },
    clientId: process.env.NUXT_HUIA_AUTH_CLIENT_ID ?? 'todo-app',
    clientSecret: process.env.NUXT_HUIA_AUTH_CLIENT_SECRET ?? 'todo-app-secret',
    scopes: ['openid', 'profile', 'email', 'offline_access'],
    // forward ?ui_locales=<locale> to /connect/authorize so the Huia account UI matches the app locale
    allowedAuthParams: ['ui_locales'],
    par: { enabled: true },
    session: {
      password: process.env.NUXT_HUIA_AUTH_SESSION_PASSWORD
        ?? 'dev-only-todo-session-password-change-me-01234567890',
      userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name'],
    },
  },
})
