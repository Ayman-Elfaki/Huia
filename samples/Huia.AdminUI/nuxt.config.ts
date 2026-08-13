// https://nuxt.com/docs/api/configuration/nuxt-config
import tailwindcss from '@tailwindcss/vite'

const issuer = process.env.HUIA_ISSUER || 'https://localhost:5041'

export default defineNuxtConfig({
  modules: [
    '@nuxt/eslint',
    'nuxt-oidc-auth',
    'nuxt-api-party'
  ],

  // Disabled: its floating panel intercepts clicks in automated (headless/Playwright) browser runs — see
  // tests/Huia.Tests.E2E/AdminUiE2ETests.cs. Toggle back on locally with Shift+Alt+D if you want it.
  devtools: {
    enabled: false
  },

  css: ['~/assets/css/main.css'],

  // @nuxt/ui used to register this itself (it bundles Tailwind v4 internally) — now that shadcn-vue owns
  // the UI layer instead, nothing else processes the `@import "tailwindcss"` in main.css without it. Its
  // absence doesn't fail the client build (Vite's dev/client CSS pipeline tolerates the unprocessed
  // `@theme`/`@utility`/`@apply` at-rules and just never generates utility classes from them), only the
  // Nitro/SSR build, which resolves `@import` through plain postcss-import instead and needs a real file.
  vite: {
    plugins: [tailwindcss()]
  },

  compatibilityDate: '2026-06-30',

  // nuxt-api-party generates its own server proxy per endpoint (composables $huiaAdmin/$huiaManage +
  // useHuiaAdminData/useHuiaManageData) — the actual Authorization header is attached per-request in
  // server/plugins/apiPartyAuth.ts, not here, since it depends on the signed-in user's session.
  apiParty: {
    endpoints: {
      huiaAdmin: { url: `${issuer}/api/identity/admin` },
      huiaManage: { url: `${issuer}/api/identity/manage` }
    }
  },

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
        // Without these, logout() still clears the local session and redirects to Huia's /connect/logout,
        // but with no post_logout_redirect_uri — the generic "oidc" provider preset (unlike keycloak/entra/
        // etc.) sets neither by default, so OpenIddict's end-session endpoint has nowhere registered to send
        // the browser back to and the user is stranded there instead of landing back in the app.
        // logoutRedirectUri matches Oidc:AdminPostLogoutRedirectUri (see samples/Huia.TodoApi/appsettings.json
        // and AppHost.cs's HUIA_POST_LOGOUT_REDIRECT_URI), the address actually registered for this client.
        logoutRedirectParameterName: 'post_logout_redirect_uri',
        logoutRedirectUri: process.env.HUIA_POST_LOGOUT_REDIRECT_URI || 'http://localhost:3100',
        // id_token_hint isn't strictly required — OpenIddict falls back to matching post_logout_redirect_uri
        // against any client that registered it, and only admin-ui registers this one — but sending it is
        // what avoids relying on that uniqueness, so requires exposing the id_token into the session to fill
        // it in (see logout.get.js: the idTokenHint key here is only populated when one exists).
        exposeIdToken: true,
        additionalLogoutParameters: { idTokenHint: '' },
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
