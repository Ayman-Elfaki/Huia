# `huia-auth-nuxt` — Technical Specification

> A first-party **Nuxt 4** authentication module for the [Huia](../dotnet/README.md) identity
> provider. OIDC Authorization Code flow with PKCE, RFC 9126 Pushed Authorization Requests,
> transparent server-side token refresh, and a dual-layer session that keeps **every token on the
> server**.

- **Package:** `huia-auth-nuxt`
- **Target:** Nuxt `>=4.0.0`, Nitro `>=2.10`, Node `>=20.11` (Web Crypto, `globalThis.crypto`)
- **Core library:** [`openid-client`](https://github.com/panva/openid-client) v6 (ESM-only, Web
  Crypto native, form-encoded token requests by design)
- **Config key:** `huiaAuth`

---

## Table of contents

1. [Overview & Architecture](#1-overview--architecture)
2. [Module Configuration](#2-module-configuration)
3. [Project File Structure](#3-project-file-structure)
4. [Server-Side Implementation (Nitro)](#4-server-side-implementation-nitro)
5. [Client-Side Implementation (Vue/Nuxt)](#5-client-side-implementation-vuenuxt)
6. [Route Protection](#6-route-protection)
7. [Module Registration](#7-module-registration)
8. [Type Declarations](#8-type-declarations)
9. [Security Considerations](#9-security-considerations)
10. [Dependencies & Testing Strategy](#10-dependencies--testing-strategy)

---

## 1. Overview & Architecture

### 1.1 Summary

`huia-auth-nuxt` performs the OAuth 2.0 **Authorization Code flow with PKCE** against a Huia tenant's
OpenID Provider (OP), completes the code exchange on the **Nitro server**, and persists the result
across two layers:

| Layer | Holds | Lifetime | Reaches the browser? |
|---|---|---|---|
| **Nitro Storage** (`useStorage()`), keyed by an opaque session id | `access_token`, `refresh_token`, `id_token`, full `id_token` claims, token expiries, scope | `session.maxAge` (default 7 d), refreshed in place | **Never** |
| **Encrypted cookie(s)**, `iron-webcrypto`-sealed, auto-chunked | the session id + a whitelisted subset of user claims (`sub`, `name`, `email`, `preferred_username`, `roles`, …) | `session.maxAge` | Yes — sealed, `HttpOnly`, `Secure`, `SameSite=Lax`, `__Host-` prefix |

The browser therefore holds **only** an opaque session pointer and a handful of display claims. An
XSS payload on the RP cannot exfiltrate an access or refresh token because neither is ever
serialised into HTML, the Nuxt payload, or a client-readable cookie.

The module also:

- **Pushes** the authorization request parameters to Huia's `/{tenant}/connect/par` back channel
  (RFC 9126) and redirects the user with only `client_id` + `request_uri`. It **falls back** to a
  signed front-channel `/connect/authorize` when the OP does not advertise a PAR endpoint.
- **Refreshes** the access token transparently inside `getUserSession()` when it is within
  `refresh.earlyRefreshSeconds` of expiry, guarded by an in-process single-flight map **and** a
  cross-worker soft lock in Nitro Storage so concurrent SSR requests never trigger duplicate
  `refresh_token` grants.
- **Hydrates** the session on the client with zero flash-of-unauthenticated-content and zero
  hydration mismatch, following the `nuxt-auth-utils` pattern: the server resolves the session once
  per request and seeds `useState('huia-auth:session')`, which is serialised into the Nuxt payload
  and read verbatim on the client — no client-side fetch on first render.

### 1.2 SSR hydration flow

```
                          ┌─────────────────────────── Nitro server ───────────────────────────┐
  Browser                 │                                                                    │
  ───────                 │   request hook            getUserSession(event)                     │
   GET /dashboard  ─────────▶  event.context ◀───────  1. readSessionCookie(event)              │
   Cookie: __Host-huia_sess.0=…; .1=…    │                └─ parseCookies → collect .0,.1,…     │
                          │              │                   → join → iron.unseal → { sid,user }│
                          │              │             2. getTokenRecord(sid)  ── useStorage() ─┼──▶ Nitro Storage
                          │              │                   (redis / fs / memory)  ◀───────────┼──  huia-auth:sess:<sid>
                          │              │             3. if exp - now < earlyRefreshSeconds:   │      { accessToken, refreshToken,
                          │              │                   ensureFreshTokens(event, record)   │        idToken, claims, expiresAt }
                          │              │                     ├─ in-proc single-flight Map     │
                          │              │                     └─ soft lock  huia-auth:lock:<sid>┼──▶ Nitro Storage
                          │              │                        winner → refreshTokenGrant()  ┼──▶ Huia  POST /{tenant}/connect/token
                          │              │                        loser  → poll → reuse record  │
                          │              ▼                                                      │
                          │   event.context.huiaAuth = { user, loggedIn: true, expiresAt }     │
                          │        │  (NO tokens)                                               │
                          │        ▼                                                           │
                          │   Nuxt server plugin (session.server.ts)                           │
                          │     useState('huia-auth:session').value = event.context.huiaAuth   │
                          │        │                                                           │
                          │        ▼                                                           │
                          │   render <App/>  ──  useUserSession() reads the same useState ref  │
                          │        │              → middleware & templates see loggedIn=true    │
                          │        ▼              → protected page renders in full, server-side │
                          │   HTML  +  __NUXT__ payload  { state: { 'huia-auth:session': {…} }}│
                          └────────────────────────────────┬───────────────────────────────────┘
                                                           │
  Browser  ◀──────────────────────────────────────────────┘
   hydrate:  useState('huia-auth:session')  ← reads __NUXT__ payload  (NO /api/_auth/session fetch)
             useUserSession().loggedIn.value === true  on the very first client render
             ⇒ no flash-of-unauthenticated-content, no hydration mismatch
```

### 1.3 Dual-layer persistence — why

```
   ┌──────────────────────────────┐          ┌───────────────────────────────────────────────┐
   │  Encrypted cookie (browser)  │          │  Nitro Storage (server, useStorage('huia-auth'))│
   │                              │          │                                               │
   │  __Host-huia_sess[.N]        │          │  huia-auth:sess:<sid>   → TokenRecord          │
   │   iron.seal({                │  sid     │  huia-auth:lock:<sid>   → { lockId, acquiredAt }│
   │     sid: "b3f1…",   ────────────────────▶  huia-auth:state:<state>→ AuthStateRecord      │
   │     user: { sub, name, … },  │  (opaque │  huia-auth:discovery:<issuer> → cached metadata│
   │     exp: 1764700000          │  pointer)│                                               │
   │   })                         │          │  TokenRecord = {                              │
   │                              │          │    accessToken, refreshToken, idToken,        │
   │  ≤ 3800 bytes/chunk,         │          │    claims, scope,                             │
   │  HttpOnly Secure SameSite=Lax│          │    accessTokenExpiresAt, refreshTokenExpiresAt│
   └──────────────────────────────┘          │  }                                            │
                                             └───────────────────────────────────────────────┘

   • Browsers cap a single cookie near 4 KB → the sealed blob is split into
     __Host-huia_sess.0, .1, … and reassembled server-side (§4.6).
   • Tokens live only in storage → an eviction (TTL, redis flush, deploy) simply logs the
     user out; a stale cookie with no matching record is treated as anonymous, never an error.
   • Storage is the single writer of tokens → refresh rotates them in place; the cookie never
     needs rewriting on refresh (only the sid + display claims are in it).
```

---

## 2. Module Configuration

### 2.1 `nuxt.config.ts`

```ts
export default defineNuxtConfig({
  modules: ['huia-auth-nuxt'],

  huiaAuth: {
    // ── Which Huia OP ────────────────────────────────────────────────────────────
    huia: {
      baseUrl: 'https://id.example.com',   // NUXT_HUIA_AUTH_HUIA_BASE_URL
      tenant: 'acme',                      // NUXT_HUIA_AUTH_HUIA_TENANT
      // issuer: 'https://id.example.com/acme',  // overrides baseUrl+tenant when set
    },

    // ── Client credentials (confidential client) ─────────────────────────────────
    clientId: 'acme-web',
    // clientSecret: env NUXT_HUIA_AUTH_CLIENT_SECRET  (never inline in prod)
    redirectUrl: '/auth/oidc/callback',    // path only; origin is resolved per-request
    scopes: ['openid', 'profile', 'email', 'offline_access'],   // 'roles' for admin apps
    // extraAuthParams: forwarded from ?…  — allowlist below
    allowedAuthParams: ['ui_locales', 'prompt', 'login_hint'],

    // ── Pushed Authorization Requests (RFC 9126) ─────────────────────────────────
    par: {
      enabled: true,      // use PAR when the OP advertises pushed_authorization_request_endpoint
      required: false,    // set true to mirror a Huia client with the ft:par requirement
    },

    // ── Session (cookie + storage) ──────────────────────────────────────────────
    session: {
      name: '__Host-huia_sess',            // base cookie name; auto-downgrades over http (dev)
      // password: env NUXT_HUIA_AUTH_SESSION_PASSWORD  (>= 32 chars, iron-webcrypto seal key)
      maxAge: 60 * 60 * 24 * 7,            // 7 days — cookie Max-Age and storage TTL
      cookie: { sameSite: 'lax', secure: undefined },   // secure defaults to isHttps(event)
      userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
    },

    // ── Nitro Storage mount ─────────────────────────────────────────────────────
    storage: {
      base: 'huia-auth',   // useStorage() key prefix; driver is configured via nitro.storage
    },

    // ── Automatic refresh ──────────────────────────────────────────────────────
    refresh: {
      enabled: true,
      earlyRefreshSeconds: 60,             // refresh when accessTokenExpiresAt - now < 60s
      lock: { ttlMs: 10_000, waitMs: 8_000, pollMs: 150 },
    },

    // ── Cookie chunking ────────────────────────────────────────────────────────
    cookie: { chunkSize: 3800, maxChunks: 8 },   // 8 × 3800 ≈ 30 KB sealed budget

    // ── Route middleware ───────────────────────────────────────────────────────
    middleware: { global: false, exclude: [] },

    // ── Dev only ───────────────────────────────────────────────────────────────
    allowInsecureTls: false,   // honoured ONLY when import.meta.dev; throws otherwise
  },

  // The token store MUST be shared across instances in a multi-node deployment, or the soft
  // lock and the token record are per-worker and sessions "flap". In-memory is fine for a
  // single instance / local dev.
  nitro: {
    storage: {
      'huia-auth': { driver: 'redis', url: process.env.REDIS_URL },
    },
    devStorage: {
      'huia-auth': { driver: 'fs', base: '.data/huia-auth' },
    },
  },
})
```

### 2.2 Environment variables

| Variable | Required | Notes |
|---|---|---|
| `NUXT_HUIA_AUTH_CLIENT_SECRET` | yes (confidential client) | Maps to `runtimeConfig.huiaAuth.clientSecret`. |
| `NUXT_HUIA_AUTH_SESSION_PASSWORD` | yes | ≥ 32 chars. `iron-webcrypto` seal/unseal key. Rotating it invalidates every existing cookie (users re-authenticate). |
| `NUXT_HUIA_AUTH_HUIA_BASE_URL` | — | Overrides `huiaAuth.huia.baseUrl`. |
| `NUXT_HUIA_AUTH_HUIA_TENANT` | — | Overrides `huiaAuth.huia.tenant`. |
| `NODE_TLS_REJECT_UNAUTHORIZED=0` | dev only | Lets Node's undici accept the ASP.NET Core dev certificate. The module also honours `allowInsecureTls: true` (dev only) which installs a permissive `undici.Agent` as `openid-client`'s `customFetch`. **Never set either in production.** |

`runtimeConfig.public.huiaAuth` contains **only** `{ loginPath, logoutPath, sessionPath }` — no
issuer, client id, or secret is exposed to the browser (every protocol step is a server route).

### 2.3 The Huia token endpoint is form-encoded

OpenIddict (Huia's protocol engine) accepts **`application/x-www-form-urlencoded` only** on
`/{tenant}/connect/token` and `/{tenant}/connect/par`; it rejects `multipart/form-data`.
`openid-client` v6 sends form-encoded bodies by design, so **no configuration is required** — this
is called out only because the previous `nuxt-oidc-auth` integration needed an explicit
`tokenRequestType: 'form-urlencoded'` override.

---

## 3. Project File Structure

### 3.1 Consumer app (Nuxt 4 `app/` + `server/`)

```
your-app/
├─ nuxt.config.ts                 modules: ['huia-auth-nuxt'],  huiaAuth: { … }
├─ app/
│  ├─ pages/
│  │  ├─ index.vue                public
│  │  └─ dashboard.vue            definePageMeta({ middleware: 'auth' })
│  └─ components/
│     └─ UserMenu.vue             const { user, loggedIn } = useUserSession()
└─ server/
   ├─ api/
   │  └─ me.get.ts                const { user } = await requireUserSession(event)
   └─ plugins/
      └─ upstream-auth.ts         defineNitroPlugin: attach getAccessToken(event) to upstream calls
```

The module contributes the routes `/auth/oidc/login`, `/auth/oidc/callback`, `/auth/oidc/logout`
and `/api/_auth/session` (all configurable), the `auth` route middleware, the auto-imported
composables `useUserSession` / `useAuth`, and the auto-imported server utilities
`getUserSession` / `setUserSession` / `clearUserSession` / `requireUserSession` / `getAccessToken`.

### 3.2 Module internals (`src/runtime/`)

```
src/nuxt/
├─ package.json                   name: "huia-auth-nuxt"  (@nuxt/module-builder)
├─ build.config.ts
├─ tsconfig.json
├─ src/
│  ├─ module.ts                   defineNuxtModule — §7
│  └─ runtime/
│     ├─ server/
│     │  ├─ plugins/
│     │  │  └─ oidc.discovery.ts      Nitro plugin: OIDC discovery + cache, PAR capability, dev-TLS fetch
│     │  ├─ middleware/
│     │  │  └─ session.context.ts     Nitro route middleware: resolves the session onto event.context.huiaAuth
│     │  ├─ routes/
│     │  │  └─ auth/oidc/
│     │  │     ├─ login.get.ts        begin Authorization Code + PKCE (+ PAR)
│     │  │     ├─ callback.get.ts     code exchange, create session, redirect to returnTo
│     │  │     └─ logout.get.ts       clear session + RP-initiated end_session
│     │  ├─ api/_auth/
│     │  │  └─ session.get.ts         sanitised UserSession JSON, Cache-Control: no-store
│     │  └─ utils/
│     │     ├─ config.ts              resolveAuthConfig(event) — issuer, redirectUri, names, timings
│     │     ├─ oidc.ts                client factory, beginAuthorization, completeAuthorization, buildLogoutUrl
│     │     ├─ cookie.ts              seal/unseal + chunk write/read/clear
│     │     ├─ storage.ts             TokenRecord CRUD, key helpers, newSessionId()
│     │     ├─ refresh.ts             ensureFreshTokens — single-flight + soft lock
│     │     ├─ session.ts             get/set/clear/requireUserSession, getAccessToken
│     │     ├─ tokens.ts              pickUserClaims, assertHuiaIssuer, isExpired, sanitizeReturnTo
│     │     └─ internal-types.ts      TokenRecord, AuthStateRecord, LockRecord, ResolvedAuthConfig
│     ├─ app/
│     │  ├─ plugins/
│     │  │  └─ session.server.ts      seed useState('huia-auth:session') from event.context.huiaAuth
│     │  ├─ composables/
│     │  │  ├─ useUserSession.ts
│     │  │  └─ useAuth.ts
│     │  ├─ middleware/
│     │  │  └─ auth.ts                addRouteMiddleware('auth', …)
│     │  └─ utils/
│     │     └─ paths.ts
│     ├─ types.ts                     public: UserClaims / UserSession / UserSessionRequired / SecureSessionData
│     └─ types.d.ts                   module augmentations (#huia-auth, h3, nitropack, @nuxt/schema)
├─ playground/                        a runnable consumer app (index + protected page, server/api/whoami)
└─ test/
   ├─ unit/                           cookie-chunking, seal-unseal, pkce-state, soft-lock, refresh-expired
   ├─ integration/                    login-par, login-par-fallback, callback-state-mismatch, session-hydration, refresh
   └─ fixtures/mock-op/               an h3 app emulating Huia's discovery / authorize / token / par / userinfo / logout
```

---

## 4. Server-Side Implementation (Nitro)

All `openid-client` v6 symbols below come from `import * as oidc from 'openid-client'`.

### 4.1 OIDC discovery — Nitro plugin

`src/runtime/server/plugins/oidc.discovery.ts` discovers the OP **once per issuer per worker** and
caches the (non-serialisable) `Configuration` object in module scope. It also snapshots the
serialisable `server_metadata` into Nitro Storage so a freshly-spawned worker can warm-start without
a network round-trip.

```ts
import * as oidc from 'openid-client'
import { defineNitroPlugin, useRuntimeConfig, useStorage } from '#imports'
import { resolveIssuer, discoveryCacheKey } from '../utils/config'
import { oidcFetch } from '../utils/oidc'

const configs = new Map<string, oidc.Configuration>()
const inflight = new Map<string, Promise<oidc.Configuration>>()

export async function getOidcConfig(issuer: string): Promise<oidc.Configuration> {
  const cached = configs.get(issuer)
  if (cached) return cached

  const pending = inflight.get(issuer)
  if (pending) return pending

  const p = discoverInternal(issuer)
  inflight.set(issuer, p)
  try {
    const cfg = await p
    configs.set(issuer, cfg)
    return cfg
  } finally {
    inflight.delete(issuer)
  }
}

async function discoverInternal(issuer: string): Promise<oidc.Configuration> {
  const { huiaAuth } = useRuntimeConfig()
  const server = new URL(`${issuer}/.well-known/openid-configuration`)

  const execute: Array<(cfg: oidc.Configuration) => void> = []
  // Dev only: accept the ASP.NET Core self-signed certificate.
  if (import.meta.dev && (process.env.NODE_TLS_REJECT_UNAUTHORIZED === '0' || huiaAuth.allowInsecureTls)) {
    execute.push(oidc.allowInsecureRequests)
  } else if (huiaAuth.allowInsecureTls) {
    throw new Error('[huia-auth] allowInsecureTls is only honoured when import.meta.dev is true')
  }

  const cfg = await oidc.discovery(
    server,
    huiaAuth.clientId,
    { /* extra client metadata if ever needed */ },
    oidc.ClientSecretPost(huiaAuth.clientSecret),
    { execute, [oidc.customFetch]: oidcFetch() },
  )

  // Warm-start snapshot (best effort, short TTL).
  await useStorage(huiaAuth.storage.base)
    .setItem(discoveryCacheKey(issuer), cfg.serverMetadata(), { ttl: 3600 })
    .catch(() => {})

  return cfg
}

export default defineNitroPlugin(async () => {
  const { huiaAuth } = useRuntimeConfig()
  const issuer = resolveIssuer(huiaAuth)
  // Kick discovery off eagerly; do not block route handling if the OP is briefly unreachable.
  getOidcConfig(issuer).then(
    (cfg) => {
      const par = !!cfg.serverMetadata().pushed_authorization_request_endpoint
      console.info(`[huia-auth] issuer=${issuer} PAR=${par ? 'yes' : 'no'} refresh=${huiaAuth.refresh.enabled}`)
    },
    (err) => console.warn(`[huia-auth] discovery deferred for ${issuer}: ${(err as Error).message}`),
  )
})
```

**Edge cases**

- **OP briefly unreachable at boot** — the plugin does not throw; the first `login.get.ts` call
  `await`s `getOidcConfig()` which retries discovery. A hard failure there returns a
  `502 discovery_unavailable` (never a stack trace).
- **Issuer mismatch** — `oidc.discovery()` itself asserts `metadata.issuer === issuer`; a
  misconfigured `baseUrl`/`tenant` fails fast with a clear message.

### 4.2 Login endpoint — `GET /auth/oidc/login` (with PAR)

`src/runtime/server/routes/auth/oidc/login.get.ts`

```ts
import { defineEventHandler, getQuery, sendRedirect } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { beginAuthorization } from '../../../utils/oidc'
import { sanitizeReturnTo } from '../../../utils/tokens'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const q = getQuery(event)
  const returnTo = sanitizeReturnTo(typeof q.returnTo === 'string' ? q.returnTo : undefined)

  // Allow-listed passthrough params (e.g. ?ui_locales=ar to localise the Huia account UI).
  const extra: Record<string, string> = {}
  for (const key of cfg.allowedAuthParams) {
    const v = q[key]
    if (typeof v === 'string' && v.length > 0) extra[key] = v
  }

  try {
    const { redirectTo } = await beginAuthorization(event, { returnTo, extra })
    return sendRedirect(event, redirectTo, 302)
  } catch (err) {
    // Never leak internals to the browser.
    const reason = err instanceof Error && err.message.startsWith('par_required') ? 'par_required' : 'login_failed'
    return sendRedirect(event, `${cfg.errorPath}?auth_error=${reason}`, 302)
  }
})
```

`beginAuthorization` (in `utils/oidc.ts`) is where PKCE, state/nonce, PAR and the front-channel
fallback live:

```ts
import * as oidc from 'openid-client'
import { getRequestURL, setCookie, type H3Event } from 'h3'
import { useStorage } from '#imports'
import { resolveAuthConfig } from './config'
import { getOidcConfig } from '../plugins/oidc.discovery'
import { sealValue } from './cookie'
import { stateKey } from './storage'
import type { AuthStateRecord } from './internal-types'

export async function beginAuthorization(
  event: H3Event,
  opts: { returnTo: string, extra: Record<string, string> },
): Promise<{ redirectTo: string }> {
  const cfg = resolveAuthConfig(event)
  const config = await getOidcConfig(cfg.issuer)
  const meta = config.serverMetadata()

  const codeVerifier = oidc.randomPKCECodeVerifier()
  const codeChallenge = await oidc.calculatePKCECodeChallenge(codeVerifier)
  const state = oidc.randomState()
  const nonce = oidc.randomNonce()

  const params: Record<string, string> = {
    client_id: cfg.clientId,
    redirect_uri: cfg.redirectUri,
    response_type: 'code',
    scope: cfg.scopes.join(' '),
    state,
    nonce,
    code_challenge: codeChallenge,
    code_challenge_method: 'S256',
    ...opts.extra,
  }

  let redirectTo: string
  let parExpiresAt: number | undefined

  const parSupported = !!meta.pushed_authorization_request_endpoint
  if (cfg.par.enabled && parSupported) {
    try {
      // Back-channel POST to https://id.example.com/acme/connect/par (form-encoded, client-authenticated).
      const par = await oidc.pushedAuthorizationRequest(config, params)
      parExpiresAt = Date.now() + (Number(par.expires_in) || 60) * 1000
      const url = new URL(meta.authorization_endpoint!)
      url.searchParams.set('client_id', cfg.clientId)
      url.searchParams.set('request_uri', String(par.request_uri))
      redirectTo = url.href
    } catch (err) {
      if (cfg.par.required) throw new Error('par_required: PAR is mandatory for this client but the push failed', { cause: err })
      redirectTo = oidc.buildAuthorizationUrl(config, params).href   // graceful fallback
    }
  } else if (cfg.par.required) {
    throw new Error('par_required: the OP does not advertise a PAR endpoint')
  } else {
    redirectTo = oidc.buildAuthorizationUrl(config, params).href
  }

  // Persist the flow state server-side; TTL covers the PAR window plus slack.
  const record: AuthStateRecord = {
    state, nonce, codeVerifier,
    redirectUri: cfg.redirectUri,
    returnTo: opts.returnTo,
    createdAt: Date.now(),
    parExpiresAt,
  }
  const ttl = Math.min(600, Math.ceil(((parExpiresAt ?? Date.now() + 300_000) - Date.now()) / 1000) + 60)
  await useStorage(cfg.storageBase).setItem(stateKey(cfg, state), record, { ttl })

  // Defence-in-depth: bind `state` to a short-lived sealed cookie too (checked at the callback).
  setCookie(event, cfg.oauthCookieName, await sealValue(cfg, { state }), {
    httpOnly: true, secure: cfg.secure, sameSite: 'lax', path: '/', maxAge: ttl,
  })

  return { redirectTo }
}
```

**Edge cases**

| Situation | Handling |
|---|---|
| OP has no `pushed_authorization_request_endpoint` | Front-channel `buildAuthorizationUrl` fallback (unless `par.required`, then `par_required`). |
| PAR push returns 4xx/5xx | Same fallback; `par.required` → `par_required` error → `?auth_error=par_required` + retry link. |
| PAR `request_uri` expires (Huia: 90 s) before the browser reaches `/authorize` | The OP rejects with `error=invalid_request`; the user lands back on the callback with `?error=…` → mapped to `?auth_error=par_expired`. The state record TTL (`expires_in + 60`) lets it be GC'd. PAR is pushed **inside this handler only**, never on page load, so the window is just browser latency. |
| `returnTo` is `//evil.com`, `https://evil.com`, or a protocol-relative URL | `sanitizeReturnTo` returns `/`. |

### 4.3 Callback endpoint — `GET /auth/oidc/callback`

`src/runtime/server/routes/auth/oidc/callback.get.ts`

```ts
import { defineEventHandler, sendRedirect } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { completeAuthorization } from '../../../utils/oidc'
import { setUserSession } from '../../../utils/session'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  try {
    const { tokens, claims, returnTo } = await completeAuthorization(event)
    await setUserSession(event, { tokens, claims })
    return sendRedirect(event, returnTo || '/', 302)
  } catch (err) {
    const code =
      err instanceof Error && /state/i.test(err.message) ? 'state_mismatch'
      : err instanceof Error && /issuer/i.test(err.message) ? 'issuer_mismatch'
      : err instanceof Error && /expired_request_uri|invalid_request/i.test(err.message) ? 'par_expired'
      : 'callback_failed'
    return sendRedirect(event, `${cfg.errorPath}?auth_error=${code}`, 302)
  }
})
```

```ts
// utils/oidc.ts — completeAuthorization
export async function completeAuthorization(event: H3Event): Promise<{
  tokens: oidc.TokenEndpointResponse & oidc.TokenEndpointResponseHelpers
  claims: oidc.IDToken
  returnTo: string
}> {
  const cfg = resolveAuthConfig(event)
  const config = await getOidcConfig(cfg.issuer)
  const url = getRequestURL(event)

  const state = url.searchParams.get('state') ?? ''
  const record = await useStorage(cfg.storageBase).getItem<AuthStateRecord>(stateKey(cfg, state))
  const cookieState = (await unsealValue<{ state: string }>(cfg, getCookie(event, cfg.oauthCookieName)))?.state

  if (!record || !state || record.state !== state || cookieState !== state) {
    throw new Error('oauth_state_mismatch')
  }

  // openid-client validates state, nonce, PKCE, the RFC 9207 `iss` response param,
  // c_hash / at_hash, and the id_token signature + standard claims.
  const tokens = await oidc.authorizationCodeGrant(config, url, {
    pkceCodeVerifier: record.codeVerifier,
    expectedState: record.state,
    expectedNonce: record.nonce,
    idTokenExpected: true,
  })

  const claims = tokens.claims()!
  // Explicit Huia constraint: the id_token issuer is the per-tenant string, exactly.
  assertHuiaIssuer(claims.iss, cfg.issuer)

  await useStorage(cfg.storageBase).removeItem(stateKey(cfg, state)).catch(() => {})
  deleteCookie(event, cfg.oauthCookieName, { path: '/' })

  return { tokens, claims, returnTo: record.returnTo }
}
```

### 4.4 Logout endpoint — `GET /auth/oidc/logout`

```ts
import { defineEventHandler, getQuery, sendRedirect } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { getSecureTokenRecord } from '../../../utils/session'
import { clearUserSession } from '../../../utils/session'
import { buildLogoutUrl } from '../../../utils/oidc'
import { sanitizeReturnTo } from '../../../utils/tokens'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const returnTo = sanitizeReturnTo(String(getQuery(event).returnTo ?? '/'))
  const postLogoutRedirectUri = new URL(returnTo, getRequestURL(event).origin).href

  const record = await getSecureTokenRecord(event)   // read the id_token BEFORE clearing
  await clearUserSession(event)                       // local session gone unconditionally

  if (cfg.logout.rpInitiated !== false && record?.idToken) {
    return sendRedirect(event, await buildLogoutUrl(event, {
      idTokenHint: record.idToken,
      postLogoutRedirectUri,
    }), 302)
  }
  // No id_token to hand back (evicted / expired) → purely local logout.
  return sendRedirect(event, postLogoutRedirectUri, 302)
})
```

```ts
// utils/oidc.ts — buildLogoutUrl
export async function buildLogoutUrl(
  event: H3Event,
  opts: { idTokenHint?: string, postLogoutRedirectUri: string },
): Promise<string> {
  const cfg = resolveAuthConfig(event)
  const config = await getOidcConfig(cfg.issuer)
  const endpoint = config.serverMetadata().end_session_endpoint ?? `${cfg.issuer}/connect/logout`
  const url = new URL(endpoint)
  url.searchParams.set('post_logout_redirect_uri', opts.postLogoutRedirectUri)
  url.searchParams.set('client_id', cfg.clientId)
  if (opts.idTokenHint && !isWildlyExpired(opts.idTokenHint)) {
    url.searchParams.set('id_token_hint', opts.idTokenHint)
  } else if (opts.idTokenHint) {
    console.warn('[huia-auth] logout: id_token_hint omitted (expired); relying on OP session cookie')
  }
  return url.href
}
```

### 4.5 Session utilities — `getUserSession` / `setUserSession` / `clearUserSession` / `requireUserSession` / `getAccessToken`

`src/runtime/server/utils/session.ts` — auto-imported in `server/**` via `addServerImportsDir`.

```ts
import { createError, getContext, type H3Event } from 'h3'
import { resolveAuthConfig } from './config'
import { readSessionCookie, writeSessionCookie, clearSessionCookies } from './cookie'
import { getTokenRecord, setTokenRecord, deleteTokenRecord, deleteLock, newSessionId } from './storage'
import { ensureFreshTokens, RefreshTokenExpiredError } from './refresh'
import { pickUserClaims } from './tokens'
import type { UserSession, UserSessionRequired } from '../../types'
import type { TokenRecord } from './internal-types'

const CTX = 'huiaAuth'

export async function getUserSession(event: H3Event): Promise<UserSession> {
  const memo = event.context[CTX] as UserSession | undefined
  if (memo) return memo

  const cfg = resolveAuthConfig(event)
  const empty: UserSession = {}

  const payload = await readSessionCookie(event, cfg)
  if (!payload) return (event.context[CTX] = empty)

  let record = await getTokenRecord(cfg, payload.sid)
  if (!record) {
    // Storage evicted the tokens (TTL, flush, logout elsewhere) → cookie is meaningless.
    await clearSessionCookies(event, cfg)
    return (event.context[CTX] = empty)
  }

  if (cfg.refresh.enabled && record.accessTokenExpiresAt - Date.now() < cfg.refresh.earlyRefreshSeconds * 1000) {
    try {
      record = await ensureFreshTokens(event, record)
    } catch (err) {
      if (err instanceof RefreshTokenExpiredError) {
        await clearUserSession(event)
        return (event.context[CTX] = empty)
      }
      throw err   // transient (5xx / network) → surface as 503 upstream, do not log the user out
    }
  }

  const session: UserSession = { user: payload.user, loggedIn: true, expiresAt: record.accessTokenExpiresAt }
  return (event.context[CTX] = session)
}

export async function setUserSession(
  event: H3Event,
  data: Partial<UserSession> & { tokens?: OidcTokens, claims?: Record<string, unknown> },
): Promise<UserSession> {
  const cfg = resolveAuthConfig(event)

  if (data.tokens && data.claims) {
    // Login: mint a fresh opaque session id (defeats fixation), write the token record, seal the cookie.
    const sid = newSessionId()
    const now = Date.now()
    const record: TokenRecord = {
      sid,
      accessToken: data.tokens.access_token,
      refreshToken: data.tokens.refresh_token,
      idToken: data.tokens.id_token,
      tokenType: 'Bearer',
      scope: data.tokens.scope ?? cfg.scopes.join(' '),
      claims: data.claims,
      accessTokenExpiresAt: now + (Number(data.tokens.expires_in) || 300) * 1000,
      refreshTokenExpiresAt: data.tokens.refresh_expires_in ? now + Number(data.tokens.refresh_expires_in) * 1000 : undefined,
      createdAt: now,
      updatedAt: now,
    }
    await setTokenRecord(cfg, record)
    const user = pickUserClaims(data.claims, cfg.session.userClaims)
    await writeSessionCookie(event, cfg, { sid, user, exp: Math.floor((now + cfg.session.maxAge * 1000) / 1000) })
    const session: UserSession = { user, loggedIn: true, expiresAt: record.accessTokenExpiresAt }
    event.context[CTX] = session
    return session
  }

  // Patch the display claims only; reuse the existing sid + token record.
  const current = await readSessionCookie(event, cfg)
  if (!current) throw createError({ statusCode: 401, statusMessage: 'No session to update' })
  const user = { ...current.user, ...(data.user ?? {}) }
  await writeSessionCookie(event, cfg, { ...current, user })
  const session: UserSession = { user, loggedIn: true, expiresAt: (event.context[CTX] as UserSession)?.expiresAt }
  event.context[CTX] = session
  return session
}

export async function clearUserSession(event: H3Event): Promise<void> {
  const cfg = resolveAuthConfig(event)
  const payload = await readSessionCookie(event, cfg)
  if (payload) {
    await deleteTokenRecord(cfg, payload.sid).catch(() => {})
    await deleteLock(cfg, payload.sid).catch(() => {})
  }
  await clearSessionCookies(event, cfg)
  event.context[CTX] = {}
}

export async function requireUserSession(event: H3Event): Promise<UserSessionRequired> {
  const session = await getUserSession(event)
  if (!session.loggedIn || !session.user) {
    throw createError({ statusCode: 401, statusMessage: 'Unauthorized', data: { code: 'auth_required' } })
  }
  return session as UserSessionRequired
}

/** Server-only. Returns a valid (refreshed if needed) access token for calling upstream APIs. */
export async function getAccessToken(event: H3Event): Promise<string | null> {
  const cfg = resolveAuthConfig(event)
  const payload = await readSessionCookie(event, cfg)
  if (!payload) return null
  let record = await getTokenRecord(cfg, payload.sid)
  if (!record) return null
  if (cfg.refresh.enabled && record.accessTokenExpiresAt - Date.now() < cfg.refresh.earlyRefreshSeconds * 1000) {
    try { record = await ensureFreshTokens(event, record) }
    catch (err) { if (err instanceof RefreshTokenExpiredError) { await clearUserSession(event); return null } throw err }
  }
  return record.accessToken
}

/** Server-only. The full token record (incl. id_token) — used by the logout handler. */
export async function getSecureTokenRecord(event: H3Event): Promise<TokenRecord | null> {
  const cfg = resolveAuthConfig(event)
  const payload = await readSessionCookie(event, cfg)
  return payload ? getTokenRecord(cfg, payload.sid) : null
}
```

Replacing the old `nuxt-oidc-auth` bearer-injection plugin becomes a one-liner:

```ts
// server/plugins/upstream-auth.ts
export default defineNitroPlugin((nitro) => {
  nitro.hooks.hook('api-party:request', async (ctx, event) => {
    const token = await getAccessToken(event)          // server-only; refreshes transparently
    if (token) {
      const headers = new Headers(ctx.options.headers as HeadersInit | undefined)
      headers.set('Authorization', `Bearer ${token}`)
      ctx.options.headers = headers
    }
  })
})
```

### 4.6 Cookie chunking algorithm

`src/runtime/server/utils/cookie.ts`. The **sealed** value is a base64url ASCII string, so byte
length equals string length and `String.prototype.slice` chunks it safely.

```ts
import { parseCookies, setCookie, deleteCookie, getRequestProtocol, type H3Event } from 'h3'
import { seal, unseal, defaults as ironDefaults } from 'iron-webcrypto'
import { subtle, getRandomValues } from 'uncrypto'
import type { ResolvedAuthConfig, CookiePayload } from './internal-types'

const crypto = { subtle, getRandomValues }

export const sealValue = (cfg: ResolvedAuthConfig, v: unknown) =>
  seal(crypto, v, cfg.session.password, { ...ironDefaults, ttl: cfg.session.maxAge * 1000 })

export async function unsealValue<T>(cfg: ResolvedAuthConfig, sealed?: string): Promise<T | null> {
  if (!sealed) return null
  try { return (await unseal(crypto, sealed, cfg.session.password, { ...ironDefaults, ttl: cfg.session.maxAge * 1000 })) as T }
  catch { return null }
}

function baseName(cfg: ResolvedAuthConfig): string {
  // __Host- requires Secure + Path=/ + no Domain. Downgrade over plain http (dev only).
  if (!cfg.secure) return cfg.session.name.replace(/^__Host-/, '').replace(/^__Secure-/, '')
  return cfg.session.name
}

function cookieOpts(cfg: ResolvedAuthConfig) {
  return { httpOnly: true, secure: cfg.secure, sameSite: 'lax' as const, path: '/', maxAge: cfg.session.maxAge }
}

/* ── WRITE ──────────────────────────────────────────────────────────────────── */
export async function writeSessionCookie(event: H3Event, cfg: ResolvedAuthConfig, payload: CookiePayload): Promise<void> {
  const sealed = await sealValue(cfg, payload)
  const name = baseName(cfg)
  const limit = cfg.cookie.chunkSize
  const prevCount = countChunks(event, name, cfg.cookie.maxChunks)

  if (sealed.length <= limit && prevCount <= 1) {
    setCookie(event, name, sealed, cookieOpts(cfg))
    clearChunksFrom(event, cfg, name, 0)          // drop any stale .0, .1, …
    return
  }

  const parts: string[] = []
  for (let i = 0; i < sealed.length; i += limit) parts.push(sealed.slice(i, i + limit))
  if (parts.length > cfg.cookie.maxChunks) {
    throw createError({
      statusCode: 500,
      statusMessage: 'session_cookie_too_large',
      message: `Sealed session is ${sealed.length}B (> ${cfg.cookie.maxChunks} × ${limit}). `
             + 'Trim huiaAuth.session.userClaims or raise huiaAuth.cookie.maxChunks.',
    })
  }
  parts.forEach((part, i) => setCookie(event, `${name}.${i}`, part, cookieOpts(cfg)))
  deleteCookie(event, name, { path: '/' })         // was previously unchunked
  clearChunksFrom(event, cfg, name, parts.length)  // drop stale higher-index chunks
}

/* ── READ ───────────────────────────────────────────────────────────────────── */
export async function readSessionCookie(event: H3Event, cfg: ResolvedAuthConfig): Promise<CookiePayload | null> {
  const name = baseName(cfg)
  const cookies = parseCookies(event)

  if (cookies[name]) return unsealValue<CookiePayload>(cfg, cookies[name])   // unchunked fast path

  const parts: string[] = []
  for (let i = 0; i < cfg.cookie.maxChunks; i++) {
    const v = cookies[`${name}.${i}`]
    if (v === undefined) break        // first gap → stop; a missing chunk ⇒ incomplete ⇒ null below
    parts.push(v)
  }
  if (parts.length === 0) return null

  // A truncated / reordered / tampered assembly fails the AEAD check inside unseal → null.
  return unsealValue<CookiePayload>(cfg, parts.join(''))
}

/* ── CLEAR ──────────────────────────────────────────────────────────────────── */
export async function clearSessionCookies(event: H3Event, cfg: ResolvedAuthConfig): Promise<void> {
  const name = baseName(cfg)
  deleteCookie(event, name, { path: '/' })
  clearChunksFrom(event, cfg, name, 0)
}

/* ── helpers ────────────────────────────────────────────────────────────────── */
function countChunks(event: H3Event, name: string, max: number): number {
  const cookies = parseCookies(event)
  if (cookies[name]) return 1
  let n = 0
  while (n < max && cookies[`${name}.${n}`] !== undefined) n++
  return n
}
function clearChunksFrom(event: H3Event, cfg: ResolvedAuthConfig, name: string, from: number): void {
  const cookies = parseCookies(event)
  for (let i = from; i < cfg.cookie.maxChunks; i++) {
    if (cookies[`${name}.${i}`] !== undefined) deleteCookie(event, `${name}.${i}`, { path: '/' })
  }
}
```

**Guarantees**

| Scenario | Result |
|---|---|
| Every chunk present, in order | Reassembled, unsealed, returned. |
| A middle chunk (`.1`) missing | Loop stops at the gap → `parts` incomplete → `unseal` AEAD fails → `null` → treated as **anonymous**, never an error page. |
| `.0` missing | `parts.length === 0` → `null`. |
| Extra junk chunk after a gap | Ignored (loop already stopped). |
| Any chunk tampered / truncated / reordered by hand | `unseal` fails → `null`. |
| Session shrinks from 3 chunks to 1 | Write path deletes `.1`, `.2` (and the unchunked name if it re-appears). |
| Sealed blob exceeds `chunkSize × maxChunks` | `500 session_cookie_too_large` at login with an actionable message. Default whitelist ≈ 200–600 B → one chunk. |
| `__Host-` over plain http (dev) | Auto-downgrade `__Host-` → bare name; production must be https. |

### 4.7 Automatic token refresh + race mitigation

`src/runtime/server/utils/refresh.ts`. Two layers of mutual exclusion:

1. **In-process single-flight** — a module-scope `Map<sid, Promise<TokenRecord>>`. Concurrent SSR
   requests **in the same worker** await one promise; only one touches storage.
2. **Cross-worker soft lock** — `huia-auth:lock:<sid>` in Nitro Storage, `{ lockId, acquiredAt }`,
   `ttlMs` default 10 s. The winner refreshes; losers poll for release then reuse the winner's
   freshly-written record.

```ts
import * as oidc from 'openid-client'
import { useStorage } from '#imports'
import type { H3Event } from 'h3'
import { randomUUID } from 'uncrypto'
import { resolveAuthConfig } from './config'
import { getOidcConfig } from '../plugins/oidc.discovery'
import { getTokenRecord, setTokenRecord, deleteTokenRecord, lockKey, tokenTtlSeconds } from './storage'
import type { ResolvedAuthConfig, TokenRecord, LockRecord } from './internal-types'

export class RefreshTokenExpiredError extends Error {
  constructor(msg = 'refresh_token_expired') { super(msg); this.name = 'RefreshTokenExpiredError' }
}

const inflight = new Map<string, Promise<TokenRecord>>()
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms))
const needsRefresh = (r: TokenRecord, cfg: ResolvedAuthConfig) =>
  r.accessTokenExpiresAt - Date.now() < cfg.refresh.earlyRefreshSeconds * 1000

export async function ensureFreshTokens(event: H3Event, record: TokenRecord): Promise<TokenRecord> {
  const cfg = resolveAuthConfig(event)
  if (!needsRefresh(record, cfg)) return record

  const sid = record.sid
  const existing = inflight.get(sid)
  if (existing) return existing

  const p = guardedRefresh(cfg, sid, record)
  inflight.set(sid, p)
  try { return await p } finally { inflight.delete(sid) }
}

async function guardedRefresh(cfg: ResolvedAuthConfig, sid: string, record: TokenRecord): Promise<TokenRecord> {
  const storage = useStorage(cfg.storageBase)
  const key = lockKey(cfg, sid)
  const lockId = randomUUID()

  const acquired = await tryAcquire(cfg, sid, lockId)
  if (acquired) {
    try {
      const fresh = await performRefresh(cfg, record)
      await setTokenRecord(cfg, fresh)
      return fresh
    } catch (err) {
      if (isInvalidGrant(err)) {
        await deleteTokenRecord(cfg, sid).catch(() => {})   // every loser now sees `null` and bails
        throw new RefreshTokenExpiredError()
      }
      throw err   // transient — leave the record intact
    } finally {
      const held = await storage.getItem<LockRecord>(key)
      if (held?.lockId === lockId) await storage.removeItem(key).catch(() => {})
    }
  }

  // Lost the race → wait for the winner, then reuse its result.
  const deadline = Date.now() + cfg.refresh.lock.waitMs
  while (Date.now() < deadline) {
    await sleep(cfg.refresh.lock.pollMs)
    const stillLocked = await storage.hasItem(key)
    if (!stillLocked) {
      const latest = await getTokenRecord(cfg, sid)
      if (latest === null) throw new RefreshTokenExpiredError('cleared_by_winner')
      if (!needsRefresh(latest, cfg)) return latest
      return guardedRefresh(cfg, sid, latest)     // winner failed transiently → retry
    }
  }
  // Winner died holding the lock → steal it if stale, else give up (very rare).
  if (await stealIfStale(cfg, sid)) {
    return guardedRefresh(cfg, sid, (await getTokenRecord(cfg, sid)) ?? record)
  }
  throw createError({ statusCode: 503, statusMessage: 'token_refresh_timeout' })
}

async function tryAcquire(cfg: ResolvedAuthConfig, sid: string, lockId: string): Promise<boolean> {
  const storage = useStorage(cfg.storageBase)
  const key = lockKey(cfg, sid)
  const cur = await storage.getItem<LockRecord>(key)
  if (cur && Date.now() - cur.acquiredAt < cfg.refresh.lock.ttlMs) return false
  await storage.setItem(key, { lockId, acquiredAt: Date.now() } satisfies LockRecord,
    { ttl: Math.ceil(cfg.refresh.lock.ttlMs / 1000) })
  // Re-read to shrink (not eliminate) the TOCTOU window on non-atomic drivers.
  return (await storage.getItem<LockRecord>(key))?.lockId === lockId
}

async function stealIfStale(cfg: ResolvedAuthConfig, sid: string): Promise<boolean> {
  const storage = useStorage(cfg.storageBase)
  const key = lockKey(cfg, sid)
  const cur = await storage.getItem<LockRecord>(key)
  if (!cur || Date.now() - cur.acquiredAt >= cfg.refresh.lock.ttlMs) {
    await storage.removeItem(key).catch(() => {})
    return true
  }
  return false
}

async function performRefresh(cfg: ResolvedAuthConfig, record: TokenRecord): Promise<TokenRecord> {
  if (!record.refreshToken) throw new RefreshTokenExpiredError('no_refresh_token')
  const config = await getOidcConfig(cfg.issuer)
  const res = await oidc.refreshTokenGrant(config, record.refreshToken, { scope: record.scope })
  const now = Date.now()
  return {
    ...record,
    accessToken: res.access_token,
    refreshToken: res.refresh_token ?? record.refreshToken,   // OpenIddict rotates by default
    idToken: res.id_token ?? record.idToken,
    claims: res.id_token ? (res.claims?.() ?? record.claims) : record.claims,
    scope: res.scope ?? record.scope,
    accessTokenExpiresAt: now + (Number(res.expires_in) || 300) * 1000,
    refreshTokenExpiresAt: res.refresh_expires_in ? now + Number(res.refresh_expires_in) * 1000 : record.refreshTokenExpiresAt,
    updatedAt: now,
  }
}

function isInvalidGrant(err: unknown): boolean {
  return err instanceof oidc.ResponseBodyError && err.error === 'invalid_grant'
}
```

**Race / failure matrix**

| Event | Outcome |
|---|---|
| N concurrent SSR requests, same worker, same `sid` | `inflight` map → **one** `refreshTokenGrant`; all N get the same record. |
| N requests across workers | First to `setItem` the lock refreshes; the rest poll `pollMs`, then read the winner's record. |
| Winner throws `invalid_grant` (revoked / rotated-and-reused / expired refresh token) | `TokenRecord` deleted; `RefreshTokenExpiredError`; `getUserSession` clears the session and returns `{}`; the SSR render completes **anonymous**; `auth` middleware then 302s to `/auth/oidc/login`. No 500 shown to the user. |
| Winner throws transient 5xx / network error | Record untouched, lock released; the error propagates as `503` (retryable). Losers retry `guardedRefresh`. |
| Winner process crashes holding the lock | Any later acquirer treats a lock older than `ttlMs` as free (`acquiredAt` check + driver `ttl` backstop). |
| Non-atomic storage driver, two winners | Both call `refreshTokenGrant`; the loser's rotated refresh token 400s → handled as transient → falls to the poll path. Bounded to **one** wasted token call. For strict correctness at scale use the `redis` driver. |

### 4.8 Session endpoint — `GET /api/_auth/session`

The one endpoint the client may call directly (used by `useUserSession().fetch()` and, if a consumer
opts into the `useAsyncData` pattern, by the hydration path).

```ts
import { defineEventHandler, setResponseHeader } from 'h3'
import { getUserSession } from '../../utils/session'

export default defineEventHandler(async (event) => {
  setResponseHeader(event, 'Cache-Control', 'no-store')
  return await getUserSession(event)   // { user?, loggedIn?, expiresAt? } — never any token
})
```

---

## 5. Client-Side Implementation (Vue/Nuxt)

### 5.1 SSR hydration plugin — the core `nuxt-auth-utils` pattern

Two pieces:

**a) A Nitro route middleware** resolves the session onto `event.context.huiaAuth` for every
request, so the value exists before the Nuxt server plugin runs.

```ts
// src/runtime/server/middleware/session.context.ts
import { defineEventHandler } from 'h3'
import { getUserSession } from '../utils/session'

export default defineEventHandler(async (event) => {
  // Populate event.context.huiaAuth (memoised inside getUserSession); swallow to never 500 a request.
  await getUserSession(event).catch(() => {})
})
```

**b) A Nuxt server plugin** copies that value into `useState`, which Nuxt serialises into the
payload. The client plugin is a no-op re-registration of the same key.

```ts
// src/runtime/app/plugins/session.server.ts   (mode: 'server')
import { defineNuxtPlugin, useState, useRequestEvent } from '#imports'
import type { UserSession } from '../../types'

export default defineNuxtPlugin(() => {
  const event = useRequestEvent()
  const state = useState<UserSession>('huia-auth:session', () => ({}))
  state.value = (event?.context.huiaAuth as UserSession | undefined) ?? {}
})
```

Because `useState('huia-auth:session')` is written **during SSR**, its value lands in `__NUXT__`.
On the client, `useUserSession()` simply returns `useState('huia-auth:session')` — the ref is
already populated from the payload. **No `onMounted`, no `useAsyncData`, no `$fetch` on first
render** ⇒ the server HTML and the first client render are identical ⇒ no hydration mismatch, and a
protected page is either fully rendered server-side or 302-redirected before any HTML is sent ⇒ no
flash-of-unauthenticated-content.

**Alternative — `useAsyncData` + `useRequestFetch`.** A consumer who prefers an explicit fetch can
disable the plugin (`huiaAuth.hydration: 'asyncData'`) and use:

```ts
const { data: session } = await useAsyncData('huia-auth:session',
  () => useRequestFetch()('/api/_auth/session'),
  { default: () => ({}) as UserSession })
```

FOUC-free **only if** all four hold: (1) the call is `await`ed so SSR blocks on it; (2) the key is
stable so the client dedupes against the payload instead of refetching; (3) `default` fixes the
shape before resolution; (4) no template branch is gated on a separate `pending` that flips on the
client. `useRequestFetch()` forwards the incoming `Cookie` header to `/api/_auth/session` during
SSR. The trade-off vs. the default is one extra internal HTTP round-trip per SSR request.

### 5.2 `useUserSession()`

```ts
// src/runtime/app/composables/useUserSession.ts
import { computed, readonly } from 'vue'
import { useState, useRequestFetch, navigateTo } from '#imports'
import type { UserSession, UserClaims } from '../../types'

export function useUserSession() {
  const session = useState<UserSession>('huia-auth:session', () => ({}))

  return {
    session: readonly(session),
    user: computed<UserClaims | null>(() => session.value.user ?? null),
    loggedIn: computed(() => !!session.value.user),
    expiresAt: computed(() => session.value.expiresAt),

    /** Re-read the server session (after returning from login, on tab focus, …). */
    async fetch() {
      session.value = await useRequestFetch()('/api/_auth/session')
    },
    /** Full logout via the server route (clears storage + cookies, hits the OP end_session). */
    async clear() {
      await navigateTo({ path: '/auth/oidc/logout', query: { returnTo: useRoute().fullPath } }, { external: true })
    },
    /** Optimistic local reset only (UI); does not touch the server. */
    clearLocal() {
      session.value = {}
    },
  }
}
```

### 5.3 `useAuth()`

```ts
// src/runtime/app/composables/useAuth.ts
import { navigateTo, useRoute } from '#imports'
import { useUserSession } from './useUserSession'

export function useAuth() {
  const { user, loggedIn, session } = useUserSession()

  return {
    user, loggedIn, session,

    login(opts: { returnTo?: string, locale?: string, prompt?: string } = {}) {
      const query: Record<string, string> = { returnTo: opts.returnTo ?? useRoute().fullPath }
      if (opts.locale) query.ui_locales = opts.locale
      if (opts.prompt) query.prompt = opts.prompt
      return navigateTo({ path: '/auth/oidc/login', query }, { external: true })
    },
    logout(opts: { returnTo?: string } = {}) {
      return navigateTo({ path: '/auth/oidc/logout', query: { returnTo: opts.returnTo ?? '/' } }, { external: true })
    },
  }
}
```

---

## 6. Route Protection

### 6.1 Client — named route middleware

```ts
// src/runtime/app/middleware/auth.ts
import { defineNuxtRouteMiddleware, navigateTo } from '#imports'
import { useUserSession } from '../composables/useUserSession'

export default defineNuxtRouteMiddleware((to) => {
  const { loggedIn } = useUserSession()
  if (loggedIn.value) return

  // Runs on the server during SSR with the correct value → no protected-content flash,
  // no client redirect bounce. `external` because /auth/oidc/login is a server route that
  // 302s off-origin to the Huia authorize endpoint.
  return navigateTo(
    { path: '/auth/oidc/login', query: { returnTo: to.fullPath } },
    { external: true, replace: true },
  )
})
```

Registered `global: false` — pages opt in:

```vue
<script setup lang="ts">
definePageMeta({ middleware: 'auth' })
</script>
```

Set `huiaAuth.middleware.global = true` to protect everything, with
`huiaAuth.middleware.exclude = ['/', '/about', '/auth/**']` for the public routes.

### 6.2 Server — API route protection

```ts
// server/api/me.get.ts
export default defineEventHandler(async (event) => {
  const { user } = await requireUserSession(event)   // 401 { code: 'auth_required' } if anonymous
  return { id: user.sub, name: user.name, roles: user.roles ?? [] }
})
```

`requireUserSession` throws `createError({ statusCode: 401, statusMessage: 'Unauthorized', data: {
code: 'auth_required' } })`. It runs the same resolve-and-refresh path as `getUserSession`, so a
protected API call also transparently refreshes a near-expired token.

### 6.3 Forwarding the access token to upstream APIs

Use `getAccessToken(event)` (server-only) — never expose the token to the client. Example with
`nuxt-api-party` in §4.5; the same pattern works for a raw `$fetch` proxy route:

```ts
// server/api/todos.get.ts
export default defineEventHandler(async (event) => {
  const token = await getAccessToken(event)
  if (!token) throw createError({ statusCode: 401 })
  return $fetch(`${useRuntimeConfig().todoApiUrl}/todos`, { headers: { Authorization: `Bearer ${token}` } })
})
```

---

## 7. Module Registration

`src/module.ts`

```ts
import { defineNuxtModule, createResolver, addServerHandler, addRouteMiddleware, addImportsDir, addServerImportsDir, addPlugin } from '@nuxt/kit'
import { defu } from 'defu'

export interface ModuleOptions {
  huia: { baseUrl?: string, tenant?: string, issuer?: string }
  clientId: string
  clientSecret?: string
  redirectUrl?: string
  scopes?: string[]
  allowedAuthParams?: string[]
  par?: { enabled?: boolean, required?: boolean }
  session?: {
    name?: string
    password?: string
    maxAge?: number
    cookie?: { sameSite?: 'lax' | 'strict' | 'none', secure?: boolean }
    userClaims?: string[]
  }
  storage?: { base?: string }
  refresh?: {
    enabled?: boolean
    earlyRefreshSeconds?: number
    lock?: { ttlMs?: number, waitMs?: number, pollMs?: number }
  }
  cookie?: { chunkSize?: number, maxChunks?: number }
  middleware?: { global?: boolean, exclude?: string[] }
  hydration?: 'useState' | 'asyncData'
  routes?: { login?: string, callback?: string, logout?: string, session?: string, error?: string }
  allowInsecureTls?: boolean
}

const defaults = {
  huia: {},
  redirectUrl: '/auth/oidc/callback',
  scopes: ['openid', 'profile', 'email', 'offline_access'],
  allowedAuthParams: ['ui_locales', 'prompt', 'login_hint'],
  par: { enabled: true, required: false },
  session: {
    name: '__Host-huia_sess',
    maxAge: 60 * 60 * 24 * 7,
    cookie: { sameSite: 'lax' as const },
    userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
  },
  storage: { base: 'huia-auth' },
  refresh: { enabled: true, earlyRefreshSeconds: 60, lock: { ttlMs: 10_000, waitMs: 8_000, pollMs: 150 } },
  cookie: { chunkSize: 3800, maxChunks: 8 },
  middleware: { global: false, exclude: [] as string[] },
  hydration: 'useState' as const,
  routes: {
    login: '/auth/oidc/login',
    callback: '/auth/oidc/callback',
    logout: '/auth/oidc/logout',
    session: '/api/_auth/session',
    error: '/',
  },
  allowInsecureTls: false,
} satisfies Partial<ModuleOptions>

export default defineNuxtModule<ModuleOptions>({
  meta: {
    name: 'huia-auth-nuxt',
    configKey: 'huiaAuth',
    compatibility: { nuxt: '>=4.0.0' },
  },
  defaults: defaults as ModuleOptions,

  setup(options, nuxt) {
    const { resolve } = createResolver(import.meta.url)
    const opts = defu(options, defaults) as Required<ModuleOptions>

    if (!opts.session.password && !process.env.NUXT_HUIA_AUTH_SESSION_PASSWORD) {
      console.warn('[huia-auth] no session password set — cookies cannot be sealed. '
        + 'Set NUXT_HUIA_AUTH_SESSION_PASSWORD (>= 32 chars).')
    }

    // ── runtime config ────────────────────────────────────────────────────────
    nuxt.options.runtimeConfig.huiaAuth = defu(nuxt.options.runtimeConfig.huiaAuth, {
      clientId: opts.clientId,
      clientSecret: opts.clientSecret ?? '',           // ← NUXT_HUIA_AUTH_CLIENT_SECRET
      issuer: opts.huia.issuer ?? '',
      huia: { baseUrl: opts.huia.baseUrl ?? '', tenant: opts.huia.tenant ?? '' },
      redirectUrl: opts.redirectUrl,
      scopes: opts.scopes,
      allowedAuthParams: opts.allowedAuthParams,
      par: opts.par,
      session: { ...opts.session, password: opts.session.password ?? '' }, // ← NUXT_HUIA_AUTH_SESSION_PASSWORD
      storage: opts.storage,
      refresh: opts.refresh,
      cookie: opts.cookie,
      routes: opts.routes,
      allowInsecureTls: opts.allowInsecureTls,
    })
    nuxt.options.runtimeConfig.public.huiaAuth = defu(nuxt.options.runtimeConfig.public.huiaAuth, {
      loginPath: opts.routes.login,
      logoutPath: opts.routes.logout,
      sessionPath: opts.routes.session,
    })

    // ── virtual type alias ────────────────────────────────────────────────────
    nuxt.options.alias['#huia-auth'] = resolve('runtime/types')

    // ── auto-imports ──────────────────────────────────────────────────────────
    addServerImportsDir(resolve('runtime/server/utils'))   // getUserSession, setUserSession, …
    addImportsDir(resolve('runtime/app/composables'))       // useUserSession, useAuth

    // ── hydration ─────────────────────────────────────────────────────────────
    if (opts.hydration === 'useState') {
      addPlugin({ src: resolve('runtime/app/plugins/session.server'), mode: 'server' })
    }

    // ── route middleware ──────────────────────────────────────────────────────
    addRouteMiddleware({
      name: 'auth',
      path: resolve('runtime/app/middleware/auth'),
      global: opts.middleware.global,
    })

    // ── server handlers ───────────────────────────────────────────────────────
    addServerHandler({ route: opts.routes.login, method: 'get', handler: resolve('runtime/server/routes/auth/oidc/login.get') })
    addServerHandler({ route: opts.routes.callback, method: 'get', handler: resolve('runtime/server/routes/auth/oidc/callback.get') })
    addServerHandler({ route: opts.routes.logout, method: 'get', handler: resolve('runtime/server/routes/auth/oidc/logout.get') })
    addServerHandler({ route: opts.routes.session, method: 'get', handler: resolve('runtime/server/api/_auth/session.get') })
    // Runs on every request: resolves event.context.huiaAuth for the hydration plugin.
    addServerHandler({ middleware: true, handler: resolve('runtime/server/middleware/session.context') })

    // ── Nitro plugin: OIDC discovery ──────────────────────────────────────────
    nuxt.hook('nitro:config', (nitro) => {
      nitro.plugins ||= []
      nitro.plugins.push(resolve('runtime/server/plugins/oidc.discovery'))
      nitro.alias ||= {}
      nitro.alias['#huia-auth'] = resolve('runtime/types')
    })

    // ── type augmentation ─────────────────────────────────────────────────────
    nuxt.hook('prepare:types', ({ references }) => {
      references.push({ path: resolve('runtime/types.d.ts') })
    })
  },
})
```

---

## 8. Type Declarations

### 8.1 Public runtime types — `src/runtime/types.ts`

```ts
/** Whitelisted, browser-visible user claims. Declaration-merge to extend. */
export interface UserClaims {
  sub: string
  name?: string
  email?: string
  preferred_username?: string
  given_name?: string
  family_name?: string
  roles?: string[]
  [key: string]: unknown
}

export interface UserSession {
  user?: UserClaims
  loggedIn?: boolean
  /** ms epoch — when the current access token expires (post-refresh). */
  expiresAt?: number
}

export interface UserSessionRequired extends UserSession {
  user: UserClaims
  loggedIn: true
}

/** Server-only. The full id_token claim set held in Nitro Storage. Declaration-merge to extend. */
export interface SecureSessionData {
  [key: string]: unknown
}
```

### 8.2 Augmentations — `src/runtime/types.d.ts`

```ts
import type { UserSession, UserSessionRequired, SecureSessionData, UserClaims } from './types'

declare module '#huia-auth' {
  export type { UserSession, UserSessionRequired, SecureSessionData, UserClaims }
}

declare module 'h3' {
  interface H3EventContext {
    /** Per-request memo of the resolved session (no tokens). */
    huiaAuth?: UserSession
  }
}

declare module 'nitropack' {
  interface NitroRuntimeHooks {
    'huia-auth:session:updated': (session: UserSession, event: import('h3').H3Event) => void
    'huia-auth:session:cleared': (event: import('h3').H3Event) => void
  }
}

declare module '@nuxt/schema' {
  interface RuntimeConfig {
    huiaAuth: {
      clientId: string
      clientSecret: string
      issuer: string
      huia: { baseUrl: string, tenant: string }
      redirectUrl: string
      scopes: string[]
      allowedAuthParams: string[]
      par: { enabled: boolean, required: boolean }
      session: { name: string, password: string, maxAge: number, cookie: Record<string, unknown>, userClaims: string[] }
      storage: { base: string }
      refresh: { enabled: boolean, earlyRefreshSeconds: number, lock: { ttlMs: number, waitMs: number, pollMs: number } }
      cookie: { chunkSize: number, maxChunks: number }
      routes: { login: string, callback: string, logout: string, session: string, error: string }
      allowInsecureTls: boolean
    }
  }
  interface PublicRuntimeConfig {
    huiaAuth: { loginPath: string, logoutPath: string, sessionPath: string }
  }
}

export {}
```

### 8.3 Consumer augmentation

```ts
// types/huia-auth.d.ts in the consuming app
declare module '#huia-auth' {
  interface UserClaims {
    tenant_id?: string
    plan?: 'free' | 'pro' | 'enterprise'
  }
  interface SecureSessionData {
    internal_user_id: string
  }
}
export {}
```

`UserClaims` and `SecureSessionData` are declared as `interface` (open) precisely so this works
project-wide.

---

## 9. Security Considerations

| Concern | Mitigation |
|---|---|
| **Authorization code interception** | PKCE `S256`; the `code_verifier` lives only in the server-side `AuthStateRecord` (Nitro Storage), never in a cookie or URL. |
| **Auth-request parameter tampering / referrer & log leakage** | RFC 9126 **PAR** — parameters pushed over the client-authenticated back channel; the browser only ever sees `client_id` + `request_uri`. |
| **OP without PAR** | Graceful fallback to a normal front-channel `/{tenant}/connect/authorize`; `par.required: true` (mirrors Huia's `ft:par`) turns the fallback into a hard error instead. |
| **CSRF on the callback** | `state` is bound to **both** the `AuthStateRecord` in storage **and** a short-lived sealed `__Host-huia_oauth` cookie; the callback requires all three (`query`, record, cookie) to match. |
| **ID token replay** | `nonce` generated per request, stored in the record, verified by `openid-client` against the `id_token`. |
| **IdP mix-up / wrong issuer** | RFC 9207 `iss` authorization-response parameter (Huia advertises `authorization_response_iss_parameter_supported`) checked by `openid-client`, **plus** an explicit `assertHuiaIssuer(claims.iss === "{baseUrl}/{tenant}")` for the per-tenant string. |
| **Token theft via XSS on the RP** | `access_token` / `refresh_token` / `id_token` are **never** serialised to HTML, the `__NUXT__` payload, or any client-readable cookie — they exist only in Nitro Storage keyed by an opaque `sid`. |
| **Session-cookie forgery / disclosure** | `iron-webcrypto` AEAD seal (`Fe26.2`), `HttpOnly`, `Secure`, `SameSite=Lax`, `__Host-` prefix (no `Domain`, `Path=/`). |
| **Cookie > 4 KB browser limit** | Deterministic chunking into `__Host-huia_sess.N`; write clears stale higher-index chunks; read stops at the first gap and any incomplete/tampered assembly fails the AEAD check → treated as anonymous. |
| **Concurrent-refresh storm / refresh-token reuse** | In-process single-flight `Map` + cross-worker soft lock in Nitro Storage; losers reuse the winner's rotated tokens and never call the token endpoint. |
| **Stale lock after a crashed worker** | Lock carries `acquiredAt`; any acquirer overwrites a lock older than `refresh.lock.ttlMs`; the storage driver `ttl` is a second backstop. |
| **Revoked / expired refresh token** | `invalid_grant` → delete the `TokenRecord` (so every other worker bails) → `RefreshTokenExpiredError` → session cleared → the request continues anonymous and the next navigation re-authenticates. |
| **Token endpoint content-type rejection (OpenIddict)** | `openid-client` v6 sends `application/x-www-form-urlencoded` natively — no `multipart/form-data`. |
| **Open redirect via `returnTo` / `post_logout_redirect_uri`** | `sanitizeReturnTo` accepts only a same-origin **path** (must start with a single `/`, reject `//`, reject any scheme). |
| **Logout with a stale `id_token_hint`** | The local session is cleared unconditionally; `end_session` is still called, with the hint omitted (+ a warn log) when the `id_token` is missing or expired. |
| **Session fixation** | A fresh random `sid` (`randomUUID`) is minted on **every** successful login; `clearUserSession` deletes the old record. |
| **Dev-TLS bypass leaking to production** | `allowInsecureTls` / `NODE_TLS_REJECT_UNAUTHORIZED=0` are honoured **only** when `import.meta.dev`; the discovery plugin throws if `allowInsecureTls` is set otherwise. |
| **PII in URLs / logs** | No tokens or claims in query strings; only the opaque `sid` travels, and only inside the sealed cookie. |
| **Multi-instance session integrity** | The spec mandates a shared Nitro Storage driver (redis) for any deployment with more than one instance, so the token record and the soft lock are global. |

---

## 10. Dependencies & Testing Strategy

### 10.1 Dependencies (`package.json`)

| Package | Range | Role |
|---|---|---|
| `@nuxt/kit` | `^4` | `defineNuxtModule` and the `add*` helpers. |
| `openid-client` | `^6.1` | OIDC/OAuth2 — discovery, PKCE, PAR, code & refresh grants. Stay on the v6 major; do **not** auto-float to a future v7. |
| `iron-webcrypto` | `^1.2` | Web-Crypto `seal`/`unseal` for the session cookie. |
| `uncrypto` | `^0.1` | Isomorphic `crypto` (`randomUUID`, `subtle`, `getRandomValues`). |
| `defu` | `^6.1` | Options + runtime-config merge. |
| `h3` | `^1.13` | Event, cookie and error helpers (also provided by Nitro; pinned for types). |
| **dev:** `@nuxt/module-builder` | `~0.8` | Build/stub the module (pin the minor — pre-1.0). |
| **dev:** `@nuxt/test-utils`, `vitest`, `nuxt`, `vue-tsc`, `@nuxt/schema` | current | Test + type-check harness. |
| **dev:** `typescript` | `~5.6` | Tilde-pinned (TS minors carry breaking type-check changes). |
| **dev:** `unstorage` | `^1.12` | Driver-backed integration tests. |
| E2E lives in the .NET `tests/Huia.E2ETests` project (Microsoft.Playwright) — see §10.5. | | |
| **peer:** `nuxt` | `^4` | |

`hookable`, `cookie-es` and `jose` are **not** needed (re-exported by Nuxt/Nitro, or done internally
by `openid-client`).

### 10.2 Unit tests (`test/unit/`, Vitest, mocked `H3Event`)

| File | Covers |
|---|---|
| `cookie-chunking.test.ts` | `slice` boundaries (exact multiple, `+1`, empty); write sets `.0…N`; shrinking clears stale `.N`; happy-path reassembly; **missing middle chunk → `null`**; **missing `.0` → `null`**; junk chunk after a gap ignored; tampered chunk → `null`; unchunked ↔ chunked transitions. |
| `seal-unseal.test.ts` | round-trip; wrong password → `null`; expired `ttl` → `null`; corrupt base64 → `null`; sealed payload contains only whitelisted keys (assert **no `access_token`**). |
| `pkce-state.test.ts` | `code_verifier` length/charset; `S256` challenge against the RFC 7636 test vector; `state`/`nonce` uniqueness over N calls; `sanitizeReturnTo` rejects `//evil`, `https://evil`, `\evil`, accepts `/x?y=1#z`. |
| `soft-lock.test.ts` | `vi.useFakeTimers()`; two concurrent `ensureFreshTokens` on one `sid` → `refreshTokenGrant` mock called **once**, both callers get the same record; a storage-lock loser polls then returns the winner's record; a stale lock (`acquiredAt` in the past) is taken over; the lock is removed only by its holder (`lockId` guard). |
| `refresh-expired.test.ts` | `refreshTokenGrant` rejects `invalid_grant` → `TokenRecord` deleted, `RefreshTokenExpiredError`; the `getUserSession` wrapper catches it, clears cookies, returns `{}`; a transient `503` propagates with the record intact and the lock released. |

### 10.3 Integration tests (`test/integration/`, `@nuxt/test-utils` `setup({ server: true })` against `playground/`)

**Mock OP** (`test/fixtures/mock-op/`) — an h3 app serving:

- `/{tenant}/.well-known/openid-configuration` (a switch includes/excludes
  `pushed_authorization_request_endpoint`)
- `/{tenant}/connect/authorize` → 302 back with `code` + `state` + `iss`
- `/{tenant}/connect/token` → asserts `Content-Type: application/x-www-form-urlencoded`; returns a
  test-JWK-signed `id_token` + `access_token` + `refresh_token`
- `/{tenant}/connect/par` → `{ request_uri: 'urn:ietf:params:oauth:request_uri:mock', expires_in: 90 }`
- `/{tenant}/connect/userinfo`, `/{tenant}/connect/logout`

| File | Asserts |
|---|---|
| `login-par.test.ts` | `GET /auth/oidc/login` → 302 to `…/authorize?client_id=…&request_uri=urn:…`; the mock OP received a back-channel `POST /par` carrying `code_challenge` + `state` + `nonce` + client auth; the browser redirect contains **no** `code_challenge`. |
| `login-par-fallback.test.ts` | discovery without a PAR endpoint → 302 to a full front-channel `…&code_challenge=…&state=…`; with `par.required: true` and PAR failing → redirect to `?auth_error=par_required`. |
| `callback-state-mismatch.test.ts` | callback with a `state` absent from storage, or not matching the `__Host-huia_oauth` cookie → `?auth_error=state_mismatch`, **no** session cookie set; an `id_token` with a wrong `iss` → `?auth_error=issuer_mismatch`. |
| `session-hydration.test.ts` | full login via the mock OP → `GET /api/_auth/session` returns `{ loggedIn: true, user: { sub, … } }` with **no** token fields; SSR-render `/` → the HTML shows the username (no FOUC) and `window.__NUXT__` carries `huia-auth:session` but no `access_token`; `/api/whoami` → 200 with the session, 401 without. |
| `refresh.test.ts` | seed a `TokenRecord` with `accessTokenExpiresAt` in the past → one `GET /api/_auth/session` → the mock OP `/token` is hit once with `grant_type=refresh_token` and `expiresAt` advances; **five parallel** requests → `/token` hit **exactly once**. |

### 10.4 Type tests

`npm run test:types` runs `vue-tsc --noEmit` on the module and the playground, plus
`test/types.test-d.ts` (`expectTypeOf`): `requireUserSession(event)` narrows `user` to non-nullable;
a consumer `declare module '#huia-auth'` augmentation merges into `UserClaims`.

### 10.5 E2E (`tests/Huia.E2ETests/HuiaAuthNuxt*`, `[Trait("Category","E2E")]`)

`HuiaAuthNuxtPlaygroundFixture` boots `Huia.IdentityServer` (`Huia__EnableE2E=true` /
`Huia__Database=Sqlite`, in-memory shared cache) on `http://localhost:5319` and runs the built
playground (`node src/nuxt/playground/.output/server/index.mjs`) on `:3030` with `NUXT_HUIA_AUTH_*`
overrides. `Program.cs` seeds a `huia-auth-nuxt-playground` confidential web client in the `e2e`
tenant with a **35 s** access-token lifetime. Skip-tolerant like the other front-end fixtures.

`HuiaAuthNuxtE2ETests` (Playwright, reuses `FrontEndFlows`):

- **Signs in, stores tokens server-side, serves a token-free session** — `/protected` bounces
  through the Huia authorize endpoint (PAR: only `request_uri` in the browser URL); after the Razor
  sign-in the page renders server-side with the user's claims; the browser holds a `huia_sess`
  cookie and no cookie whose name contains `token`; `GET /api/_auth/session` returns
  `{ loggedIn, user }` with no `accessToken` / `access_token` / `refresh*` anywhere in the body.
- **Refreshes the access token transparently** — with the 35 s lifetime and
  `earlyRefreshSeconds: 60`, two `/api/_auth/session` calls a second apart show `expiresAt`
  advancing (a real `grant_type=refresh_token` round-trip to Huia), and the page is still
  authenticated after.
- **Sign-out clears the session and re-protects the route** — `/auth/oidc/logout` round-trips
  through the OP `end_session` back to `/`, the `huia_sess*` cookies are gone, and `/protected`
  redirects to Huia again.

Not yet covered: the chunked-cookie path (a user with enough `roles` to exceed one cookie) — the
unit suite exercises chunk split/reassembly directly.

### 10.6 CI

A `nuxt-auth-module` job in `.github/workflows/ci.yml` (`working-directory: src/nuxt`):
`npm ci` → `npm run dev:prepare` → `npm run test:types` → `npm test` → `npm run dev:build` (the
last is a bundling smoke test that `openid-client` v6's ESM output survives the Nitro/Rollup build).
