const huiaBaseUrl = process.env.NUXT_PUBLIC_HUIA_BASE_URL ?? 'https://localhost:5310'
const todoApiUrl = process.env.NUXT_PUBLIC_TODO_API_URL ?? 'http://localhost:5330'
const selfUrl = process.env.NUXT_OIDC_PROVIDERS_OIDC_REDIRECT_URI?.replace('/auth/oidc/callback', '') ?? 'http://localhost:3000'

export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },
  modules: ['@nuxtjs/tailwindcss', '@nuxtjs/color-mode', '@nuxtjs/i18n', 'shadcn-nuxt', 'nuxt-api-party', 'nuxt-oidc-auth'],
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

  oidc: {
    defaultProvider: 'oidc',
    middleware: {
      globalMiddlewareEnabled: false,
    },
    session: {
      expirationCheck: true,
      automaticRefresh: true,
    },
    providers: {
      oidc: {
        clientId: process.env.NUXT_OIDC_PROVIDERS_OIDC_CLIENT_ID ?? 'todo-app',
        clientSecret: process.env.NUXT_OIDC_PROVIDERS_OIDC_CLIENT_SECRET ?? 'todo-app-secret',
        authorizationUrl: `${huiaBaseUrl}/todo/connect/authorize`,
        tokenUrl: `${huiaBaseUrl}/todo/connect/token`,
        userInfoUrl: `${huiaBaseUrl}/todo/connect/userinfo`,
        redirectUri: `${selfUrl}/auth/oidc/callback`,
        scope: ['openid', 'profile', 'email', 'offline_access'],
        exposeAccessToken: true,
        // Let the app forward ?ui_locales=<locale> to /connect/authorize so the Huia account UI
        // renders in the same language the user picked here.
        allowedClientAuthParameters: ['ui_locales'],
        // OpenIddict's token endpoint only accepts application/x-www-form-urlencoded; nuxt-oidc-auth
        // otherwise sends multipart/form-data.
        tokenRequestType: 'form-urlencoded',
        // The resource API validates the access token; the SPA session trusts the code exchange.
        tokenValidationMode: 'legacy',
        validateAccessToken: false,
        validateIdToken: false,
        logoutUrl: `${huiaBaseUrl}/todo/connect/logout`,
        logoutRedirectParameterName: 'post_logout_redirect_uri',
        additionalLogoutParameters: { idTokenHint: '' },
        optionalClaims: ['given_name', 'family_name'],
      },
    },
  },
})
