# `next-huia-oidc` — Overview

`next-huia-oidc` is the official **Next.js 14 / 15** integration library for signing a relying-party application into a `Huia.OpenId` tenant. It executes the OAuth 2.0 Authorization Code Flow with PKCE and Pushed Authorization Requests (RFC 9126 PAR) on the **Next.js Route Handlers / Server Components**, keeping tokens secure on the server.

- Package: `next-huia-oidc`
- Compatible with Next.js App Router (14+, 15+)
- Core engine: [`huia-auth-core`](/core/overview) and [`openid-client`](https://github.com/panva/openid-client) v6

## Key Features

- **Dual-layer session security**: The browser only receives an encrypted, chunked `__Host-huia_sess` cookie (`iron-webcrypto`) containing an opaque session identifier and safe claims. Raw access, refresh, and ID tokens are stored in server-side storage.
- **Pushed Authorization Requests (PAR)**: Authorization parameters are securely pushed via backchannel to Huia's `/{tenant}/connect/par` endpoint, exposing only a transient `request_uri` in the browser.
- **Transparent token refresh**: Server-side helpers automatically refresh access tokens when nearing expiration using a single-flight mutex.
- **Full App Router support**: Works across React Server Components, Server Actions, Route Handlers, and Client Components with `<HuiaOidcProvider>` and `useUserSession()`.
- **Edge / Node.js runtime**: Fully compatible with standard Node.js runtimes and Edge runtimes.

## Flow Diagram

```mermaid
sequenceDiagram
  autonumber
  actor User
  participant Browser as Next.js Client
  participant Next as Next.js Route Handler
  participant Storage as Server Storage
  participant IdP as Huia.OpenId

  User->>Browser: Click "Sign In"
  Browser->>Next: GET /api/auth/login
  Next->>IdP: POST /connect/par (Client Auth + PKCE)
  IdP-->>Next: request_uri
  Next-->>Browser: 302 Redirect to IdP /connect/authorize?request_uri=...
  Browser->>IdP: Interactive login & consent
  IdP-->>Browser: 302 Redirect to /api/auth/callback?code=...
  Browser->>Next: GET /api/auth/callback?code=...
  Next->>IdP: POST /connect/token (code + code_verifier)
  IdP-->>Next: access_token, refresh_token, id_token
  Next->>Storage: Store tokens under sid
  Next-->>Browser: Set-Cookie: __Host-huia_sess (sealed sid + claims) -> Redirect to returnTo
```

## Available Subpaths

- `next-huia-oidc/server`: Server-side Route Handler factory (`createHuiaOidcHandler`), session inspectors (`getHuiaSession`, `getAccessToken`), and middleware (`withHuiaOidcAuth`).
- `next-huia-oidc/client`: Client-side React context provider (`HuiaOidcProvider`), hook (`useUserSession`), and user context.
