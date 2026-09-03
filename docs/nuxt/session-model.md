# `huia-auth-nuxt` — session model & security

## Dual-layer persistence

| Layer | Holds | Reaches the browser? |
|---|---|---|
| **Nitro Storage** (`useStorage()`), key `sess:<sid>` | `access_token`, `refresh_token`, `id_token`, full id-token claims, expiries, scope | **never** |
| **Encrypted cookie(s)**, `iron-webcrypto`-sealed | `{ sid, user: <claim whitelist>, exp }` | yes — sealed, `HttpOnly`, `Secure`, `SameSite=Lax`, `__Host-` |

An XSS payload on the RP cannot exfiltrate a token — none is ever serialised into HTML, the Nuxt
payload, or a client-readable cookie. Storage eviction (TTL, redis flush, deploy) simply logs the
user out: a cookie with no matching record is treated as anonymous, never an error.

## Cookie chunking

The sealed payload is base64url ASCII, so it is split by length into `__Host-huia_sess.0`, `.1`, …
(the unchunked `__Host-huia_sess` is the fast path). Reads collect `.0, .1, …` and **stop at the
first gap**; any incomplete, reordered or tampered assembly fails the AEAD check and is treated as
**no session**. Writes clear stale higher-index chunks so a shrinking session never leaves danglers.
Over plain http (dev) the `__Host-` / `__Secure-` prefix is dropped (the browser would reject a
prefixed cookie without `Secure`).

## SSR hydration

```mermaid
flowchart TD
  A[Nitro request middleware] -->|getUserSession| B[read cookie → unseal → de-chunk]
  B --> C[getTokenRecord sess:sid from useStorage]
  C --> D{near expiry?}
  D -->|yes| E[ensureFreshTokens<br/>single-flight + soft lock]
  D -->|no| F[event.context.huiaAuth = { user, loggedIn, expiresAt }]
  E --> F
  F --> G[Nuxt server plugin<br/>seeds useState 'huia-auth:session']
  G --> H[SSR HTML + __NUXT__ payload]
  H --> I[client reads the same useState<br/>no fetch, no mismatch, no FOUC]
```

`useUserSession()` on the client is just `useState('huia-auth:session')` — the value came from the
SSR payload, so `loggedIn` is correct on the very first render. A `useAsyncData` +
`useRequestFetch('/api/_auth/session')` alternative is documented in the SPEC.

## Transparent refresh

Inside `getUserSession()` / `getAccessToken()`, when `accessTokenExpiresAt - now <
earlyRefreshSeconds`:

- **In-process single-flight** — a `Map<sid, Promise>` so concurrent SSR requests in one worker
  `await` a single refresh.
- **Cross-worker soft lock** — `lock:<sid>` in Nitro Storage (`{ lockId, acquiredAt }`, TTL 10 s).
  The winner calls `refreshTokenGrant` and persists the rotated tokens; losers poll for release then
  reuse the winner's record. A stale lock (older than the TTL) is taken over.
- **`invalid_grant`** (revoked / reused / expired refresh token) → delete the token record → the
  session is cleared → the request continues anonymous and the next navigation re-authenticates. No
  500 is shown.

::: warning Multi-instance
`unstorage` has no atomic CAS. Single-instance is fully correct via the in-process map; for more than
one instance use the `redis` driver — a residual double-refresh is then bounded to one wasted token
call.
:::

## Security summary

| Concern | Mitigation |
|---|---|
| code interception | PKCE S256 (`code_verifier` server-side only) |
| request tampering / referrer leak | RFC 9126 PAR back channel |
| callback CSRF | `state` bound to both the storage record and a sealed `__Host-huia_oauth` cookie |
| id-token replay | per-request `nonce` |
| IdP mix-up | RFC 9207 `iss` + an exact per-tenant issuer check (`{baseUrl}/{tenant}`) |
| token theft via XSS | tokens never leave Nitro Storage |
| cookie forgery / disclosure | `iron-webcrypto` AEAD + `HttpOnly` `Secure` `SameSite=Lax` `__Host-` |
| open redirect | `returnTo` / `post_logout_redirect_uri` sanitised to a same-origin path |
| session fixation | a fresh random `sid` on every successful login |
