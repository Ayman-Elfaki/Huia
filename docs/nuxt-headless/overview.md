# `nuxt-huia-headless` — overview

`nuxt-huia-headless` is the first-party **Nuxt 4** module for apps that authenticate against a
`Huia.Headless` backend: a plain JSON register/login API, no OIDC redirect, no discovery document. Its
sibling module, [`nuxt-huia-oidc`](/nuxt/overview), covers the multi-tenant OIDC flow against
`Huia.OpenId` instead.

- Package: `nuxt-huia-headless` · config key `huiaHeadless`
- No `openid-client` dependency — talks to `Huia.Headless` with plain `fetch`
- Same dual-layer session principle as `nuxt-huia-oidc`: the browser only ever holds a sealed,
  chunked session cookie; access/refresh tokens live server-side in Nitro Storage and are refreshed
  transparently

## Why it's a separate module, not an option on `nuxt-huia-oidc`

The two backends are different enough in kind, not just configuration, that folding one into the
other would leak abstractions both ways:

| | `Huia.OpenId` (`nuxt-huia-oidc`) | `Huia.Headless` (`nuxt-huia-headless`) |
|---|---|---|
| Protocol | OAuth 2.0 Authorization Code + PKCE, redirect-based | Plain JSON POST/GET, no redirect |
| Login UI | Hosted on the identity server (Razor pages) | Owned by the app itself — there is nothing to redirect to |
| Tokens | JWTs (OpenIddict-signed), validable by a separate resource server | Opaque bearer tokens, valid only against the app that minted them |
| Sign-out | RP-initiated `end_session` round-trip | Purely local — delete the local token, nothing to tell the server |
| Multi-tenancy | Yes — `tenant` is part of the config | No — `Huia.Headless` is single-tenant only |

## Architecture at a glance

```mermaid
flowchart LR
  B[Browser] -->|"__Host-huia_headless_sess cookie<br/>(sealed: sid + claims)"| N[Nitro server]
  N -->|"sess:&lt;sid&gt;"| S[(Nitro Storage<br/>access / refresh tokens)]
  N -->|"POST /identity/login (JSON)"| H[Huia.Headless]
  N -->|"seed useState('huia-headless-auth:session')"| B
```

The cookie is `iron-webcrypto`-sealed, `HttpOnly`, `Secure`, `SameSite=Lax`, `__Host-` prefixed (bare
name over plain http in dev), and auto-chunked past the ~4 KB per-cookie browser limit — identical
mechanics to `nuxt-huia-oidc`.

## Server routes it adds

| Route | Purpose |
|---|---|
| `POST /auth/register` | proxies `Huia.Headless`'s register endpoint — does **not** sign in |
| `POST /auth/login` | login, then fetches `identity/me` and establishes the session |
| `POST /auth/logout` | clears the local session cookie + stored tokens — no server round-trip |
| `POST /auth/refresh` | manual refresh (usually unnecessary — `getUserSession` refreshes transparently) |
| `GET /auth/session` | the sanitised `{ user, loggedIn, expiresAt }` — never a token |
| `GET /auth/confirm-email`, `POST /auth/resend-confirmation`, `/auth/forgot-password`, `/auth/reset-password` | thin proxies to the matching `Huia.Headless` endpoints |

All paths are configurable via `routes`. Unlike `nuxt-huia-oidc`'s `login`/`callback`/`logout`
(browser-navigated redirects), every one of these is a plain API call the app's own login form
invokes with `$fetch`/`useRequestFetch`.

## Composables & server utilities

- Client: `useUserSession()` (`user`, `loggedIn`, `session`, `hasRole()`, `hasAnyRole()`, `fetch()`,
  `clear()`) and `useHuia()` (`register()`, `login()`, `logout`).
- Server (auto-imported in `server/**`): `getUserSession(event)`, `setUserSession`,
  `clearUserSession`, `requireUserSession(event)` (401s), `getAccessToken(event)` (server-only —
  forward the bearer to `Huia.Headless` or an upstream API).

## Reference integration

`samples/Shop.Api` + `samples/Shop.App` puts this module through a full register → login → cart →
checkout → logout round trip, covered end to end by
[`ShopE2ETests`](https://github.com/Ayman-Elfaki/Huia/tree/main/tests/Huia.E2ETests/ShopE2ETests.cs).
