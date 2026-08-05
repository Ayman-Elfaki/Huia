// https://nuxt.com/docs/api/configuration/nuxt-config
const issuer = process.env.HUIA_ISSUER || 'https://localhost:5041'

export default defineNuxtConfig({
  modules: [
    '@nuxt/eslint',
    '@nuxt/ui',
    'nuxt-oidc-auth'
  ],

  // Disabled: its floating panel intercepts clicks in automated (headless/Playwright) browser runs — see
  // tests/Huia.Tests.E2E/AdminUiE2ETests.cs. Toggle back on locally with Shift+Alt+D if you want it.
  devtools: {
    enabled: false
  },

  css: ['~/assets/css/main.css'],

  runtimeConfig: {
    // The base URL server/api/admin/[...path].ts forwards admin-API calls to — same origin as the OIDC
    // endpoints below, just read outside the "oidc" module config key.
    huiaIssuer: issuer
  },

  compatibilityDate: '2026-06-30',

  eslint: {
    config: {
      stylistic: {
        commaDangle: 'never',
        braceStyle: '1tbs'
      }
    }
  },

  // nuxt-oidc-auth's Nuxt Kit configKey is "oidc" — read at module setup time (not just at request time via
  // runtimeConfig) to decide which provider routes to register, so it has to live at this top level rather
  // than nested under runtimeConfig.
  oidc: {
    // Huia has no dedicated preset (unlike auth0/github/keycloak/entra) — configured as the generic "oidc"
    // provider type instead. That key is fixed by nuxt-oidc-auth's own ProviderConfigs type (not an
    // arbitrary name), so the module's login/callback/logout routes are literally
    // /auth/oidc/{login,callback,logout}.
    providers: {
      oidc: {
        clientId: process.env.HUIA_CLIENT_ID || 'admin-ui',
        clientSecret: process.env.HUIA_CLIENT_SECRET || 'admin-ui-dev-secret',
        redirectUri: process.env.HUIA_REDIRECT_URI || 'http://localhost:3100/auth/oidc/callback',
        authorizationUrl: `${issuer}/connect/authorize`,
        tokenUrl: `${issuer}/connect/token`,
        userInfoUrl: `${issuer}/connect/userinfo`,
        logoutUrl: `${issuer}/connect/logout`,
        scope: ['openid', 'profile', 'email', 'offline_access', 'todos', 'roles'],
        // Default ('form') sends the token request as multipart FormData, which OpenIddict's spec-compliant
        // /connect/token endpoint rejects outright ("invalid_request: the specified Content-Type header is
        // invalid") — OAuth2's token endpoint wants application/x-www-form-urlencoded.
        tokenRequestType: 'form-urlencoded',
        // "role" is the literal claim key Huia's access/id tokens carry (OpenIddictConstants.Claims.Role
        // — singular, even though it can hold multiple values as a JSON array). Left off this list, the
        // role claim never reaches session.claims, and every user would silently look non-admin.
        optionalClaims: ['role'],
        // Huia's admin-ui client is confidential (holds a secret) and wasn't registered with
        // RequirePkce (see samples/Huia.TodoApi/Program.cs) — same reasoning todo-web's own Auth.js
        // config documents for its own client.
        pkce: false,
        // Huia's access tokens are encrypted JWEs by default (OpenIddict's default access-token format —
        // 5 dot-separated segments, "alg": "RSA-OAEP"), not plain JWTs. nuxt-oidc-auth's default token
        // parser expects a standard 3-segment JWT and fails ("Token parsing failed: Invalid token") on
        // anything else — skipAccessTokenParsing treats it as an opaque string instead. Only the id_token
        // is a real signed JWT, so validateIdToken is left at its default (true).
        skipAccessTokenParsing: true,
        validateAccessToken: false,
        // Without this, nuxt-oidc-auth just discards the raw access_token string after the callback (it's
        // only ever used transiently to read `.exp`) unless a refresh_token came back too — now that
        // "offline_access" is requested above, Huia does issue one, and nuxt-oidc-auth's own
        // automaticRefresh/expirationCheck (both on by default) use it to silently redeem a fresh access
        // token as the old one nears expiry. Left off, session.accessToken is undefined even *inside this
        // app's own server routes*, not just withheld from the browser as the option name suggests —
        // server/api/admin/[...path].ts would silently send "Authorization: Bearer undefined" on every
        // request. Same trade-off Huia.TodoApp's auth.ts makes for its own client-visible session.
        exposeAccessToken: true,
        // Validates the id_token signature against Huia's own JWKS (fetched via this discovery URL) rather
        // than trusting it blindly. A function value here looks natural given OidcProviderConfig's declared
        // type (Record<string, unknown> | (() => Promise<...>)) but does NOT work: nuxt-oidc-auth's own
        // Nuxt Kit configKey ("oidc") flows this whole object through runtimeConfig, which Nuxt serializes
        // for the Nitro server bundle — functions don't survive that, so at request time it fails with
        // "config.openIdConfiguration is not a function". The plain discovery-URL string is handled by a
        // separate runtime branch nuxt-oidc-auth's source has but its own type declaration omits.
        openIdConfiguration: `${issuer}/.well-known/openid-configuration` as unknown as Record<string, unknown>
      }
    }
  }
})
