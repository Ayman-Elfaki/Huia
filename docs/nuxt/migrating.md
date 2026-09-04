# Migrating from `nuxt-oidc-auth`

Both sample apps (`samples/Todo.App`, `samples/Huia.AdminUI`) were moved from the third-party
`nuxt-oidc-auth` beta to `huia-nuxt`. The full diff is commit `f363cec`; the shape of the change:

## `package.json`

```diff
-  "nuxt-oidc-auth": "1.0.0-beta.12",
+  "iron-webcrypto": "^1.2.1",
+  "openid-client": "^6.1.0",
+  "uncrypto": "^0.1.3",
```

Removing `nuxt-oidc-auth` also drops `typescript` from the tree (it was transitive), so add it back
and pin `vue` / `vue-router` off the floating `latest`. An `.npmrc` with `legacy-peer-deps=true`
sidesteps an npm 10 arborist crash on the nuxt peer graph.

## `nuxt.config.ts`

```diff
   modules: [
     '@nuxtjs/tailwindcss', '@nuxtjs/color-mode', 'shadcn-nuxt', 'nuxt-api-party',
-    'nuxt-oidc-auth',
+    '../../src/nuxt/src/module',   // from source in the monorepo; or 'huia-nuxt' when installed
   ],

-  oidc: {
-    defaultProvider: 'oidc',
-    session: { expirationCheck: true, automaticRefresh: true },
-    providers: { oidc: {
-      clientId: '…', clientSecret: '…',
-      authorizationUrl: `${huiaBaseUrl}/master/connect/authorize`,
-      tokenUrl: `${huiaBaseUrl}/master/connect/token`,
-      userInfoUrl: `${huiaBaseUrl}/master/connect/userinfo`,
-      redirectUri: `${selfUrl}/auth/oidc/callback`,
-      scope: ['openid', 'profile', 'email', 'roles', 'offline_access'],
-      exposeAccessToken: true,
-      tokenRequestType: 'form-urlencoded',
-      logoutUrl: `${huiaBaseUrl}/master/connect/logout`,
-      logoutRedirectParameterName: 'post_logout_redirect_uri',
-      additionalLogoutParameters: { idTokenHint: '' },
-    } },
-  },
+  huiaAuth: {
+    huia: { baseUrl: huiaBaseUrl, tenant: 'master' },
+    clientId: process.env.NUXT_HUIA_AUTH_CLIENT_ID ?? 'huia-admin-ui',
+    clientSecret: process.env.NUXT_HUIA_AUTH_CLIENT_SECRET ?? 'huia-admin-ui-secret',
+    scopes: ['openid', 'profile', 'email', 'roles', 'offline_access'],
+    par: { enabled: true },
+    session: { password: process.env.NUXT_HUIA_AUTH_SESSION_PASSWORD ?? 'dev-only-32-chars-minimum-…' },
+  },
```

The discovery-derived endpoints replace the four hand-written URLs; the redirect URI is derived
per-request from the request origin + `redirectUrl` (default `/auth/oidc/callback`, so a Huia client
registered for the old integration needs no change).

## Components & middleware

| `nuxt-oidc-auth` | `huia-nuxt` |
|---|---|
| `const { loggedIn, user, login, logout } = useOidcAuth()` | `const { loggedIn, user } = useUserSession()` + `const { login, logout } = useAuth()` |
| `user.value?.userInfo?.name` | `user.value?.name` (flat claims) |
| `login('oidc')` / `logout('oidc')` | `login()` / `logout()` |
| `login('oidc', { ui_locales })` | `login({ locale })` |
| `getUserSession(event)` in a Nitro plugin (`accessToken` on the session) | `getAccessToken(event)` (auto-imported; token stays server-side) |

## Environment (E2E / AppHost)

`NUXT_OIDC_*` → `NUXT_HUIA_AUTH_*`:

| old | new |
|---|---|
| `NUXT_OIDC_SESSION_SECRET` / `NUXT_OIDC_AUTH_SESSION_SECRET` / `NUXT_OIDC_TOKEN_KEY` | `NUXT_HUIA_AUTH_SESSION_PASSWORD` (single value, ≥ 32 chars) |
| `NUXT_OIDC_PROVIDERS_OIDC_CLIENT_SECRET` | `NUXT_HUIA_AUTH_CLIENT_SECRET` |
| the four `NUXT_OIDC_PROVIDERS_OIDC_*_URL` vars | `NUXT_HUIA_AUTH_HUIA_BASE_URL` + `NUXT_HUIA_AUTH_HUIA_TENANT` |

`NUXT_API_PARTY_ENDPOINTS_*` and `NODE_TLS_REJECT_UNAUTHORIZED=0` are unchanged.
