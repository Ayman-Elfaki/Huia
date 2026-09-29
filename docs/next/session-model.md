# `next-huia-oidc` — Session Model & Security

`next-huia-oidc` implements a zero-trust, dual-layer session architecture backed by `huia-auth-core`. It is specifically designed for Next.js App Router (React Server Components, Route Handlers, and Server Actions).

## Dual-Layer Persistence

| Layer | Holds | Reaches the Browser? |
|---|---|---|
| **Server Storage Adapter** (`MemoryStorageAdapter` or custom cache) | `access_token`, `refresh_token`, `id_token`, raw claims, expiration timestamps | **Never** |
| **Encrypted Cookie(s)** (`iron-webcrypto` AES-GCM sealed) | `{ sid, user: <claim whitelist>, exp }` | Yes — sealed, `HttpOnly`, `Secure`, `SameSite=Lax`, `__Host-` / `__Secure-` |

An XSS vulnerability in client-side React code cannot exfiltrate access or refresh tokens because tokens never touch the DOM, window storage (`localStorage`/`sessionStorage`), or client-readable cookies.

---

## Cookie Chunking & Integrity

The sealed session payload is base64url encoded. When the payload size exceeds the browser cookie size limit (default 3,800 bytes):

1. **Chunking**: The payload is split into `__Host-huia_sess.0`, `__Host-huia_sess.1`, etc.
2. **Gap Detection**: Assembly stops at the first missing chunk. Any partial, reordered, or tampered cookie assembly fails AES-GCM decryption and is discarded.
3. **Stale Cleanup**: When an updated session fits into fewer chunks, higher index chunks are explicitly deleted to prevent orphaned cookies.
4. **Environment Awareness**: Over plain HTTP (local development), `__Host-` prefixes are automatically dropped so browsers do not reject insecure development cookies.

---

## Server Components & SSR Hydration

```mermaid
flowchart TD
  A[Incoming HTTP Request] -->|getUserSession in Server Component| B[Read cookies → de-chunk → unseal]
  B --> C[Fetch TokenRecord for sid from Storage]
  C --> D{Near Expiry?}
  D -->|Yes| E[Transparent Token Refresh<br/>via Mutex / Refresh Lock]
  D -->|No| F[Return UserSession { user, loggedIn, expiresAt }]
  E --> F
  F --> G[Render Server Component HTML]
  F --> H[Pass to HuiaOidcProvider for Client Hydration]
```

In Server Components, `getUserSession()` can be called directly without any client-side network waterfall:

```tsx
// app/dashboard/page.tsx
import { getUserSession } from 'next-huia-oidc/server'
import { config } from '@/auth.config'

export default async function DashboardPage() {
  const session = await getUserSession(config)

  if (!session.loggedIn) {
    return <p>Please sign in</p>
  }

  return <h1>Welcome back, {session.user.name}</h1>
}
```

---

## Transparent Token Refresh

When an access token approaches expiration (`accessTokenExpiresAt - now < earlyRefreshSeconds`):

- **In-process single-flight**: Concurrent server requests within the same Node.js process share an in-flight Promise for the token refresh, preventing race conditions and duplicated refresh calls.
- **`invalid_grant` Handling**: If a refresh token has been revoked, rotated, or expired, the session store drops the record and clears the session gracefully, prompting a fresh login rather than crashing with an unhandled exception.
