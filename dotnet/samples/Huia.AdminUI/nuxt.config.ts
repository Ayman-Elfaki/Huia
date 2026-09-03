const huiaBaseUrl = process.env.NUXT_PUBLIC_HUIA_BASE_URL ?? 'https://localhost:5310'
const selfUrl = process.env.NUXT_OIDC_PROVIDERS_OIDC_REDIRECT_URI?.replace('/auth/oidc/callback', '') ?? 'http://localhost:3001'

export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },
  modules: ['@nuxtjs/tailwindcss', '@nuxtjs/color-mode', 'shadcn-nuxt', 'nuxt-api-party', 'nuxt-oidc-auth'],
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

  // Server-side proxy to the master-tenant APIs; the bearer token is added by
  // server/plugins/api-party-auth.ts and never reaches the browser.
  apiParty: {
    endpoints: {
      huia: { url: `${huiaBaseUrl}/master` },
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
        clientId: process.env.NUXT_OIDC_PROVIDERS_OIDC_CLIENT_ID ?? 'huia-admin-ui',
        clientSecret: process.env.NUXT_OIDC_PROVIDERS_OIDC_CLIENT_SECRET ?? 'huia-admin-ui-secret',
        authorizationUrl: `${huiaBaseUrl}/master/connect/authorize`,
        tokenUrl: `${huiaBaseUrl}/master/connect/token`,
        userInfoUrl: `${huiaBaseUrl}/master/connect/userinfo`,
        redirectUri: `${selfUrl}/auth/oidc/callback`,
        scope: ['openid', 'profile', 'email', 'roles', 'offline_access'],
        exposeAccessToken: true,
        // OpenIddict's token endpoint only accepts application/x-www-form-urlencoded; nuxt-oidc-auth
        // otherwise sends multipart/form-data.
        tokenRequestType: 'form-urlencoded',
        tokenValidationMode: 'legacy',
        validateAccessToken: false,
        validateIdToken: false,
        logoutUrl: `${huiaBaseUrl}/master/connect/logout`,
        logoutRedirectParameterName: 'post_logout_redirect_uri',
        additionalLogoutParameters: { idTokenHint: '' },
      },
    },
  },
})
