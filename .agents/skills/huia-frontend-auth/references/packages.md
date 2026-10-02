# Huia Frontend Configuration Options Reference

Configuration options and schema specifications for `next-huia-oidc`, `nuxt-huia-oidc`, and `huia-auth-core`.

---

## 1. `HuiaOidcConfig` Schema (Next.js & Nuxt)

| Option | Type | Required | Description |
|---|---|---|---|
| `baseUrl` | `string` | Yes | Base URL of the Huia Identity Server (e.g., `https://id.example.com`). |
| `tenant` | `string` | Yes | Identifier of the tenant (e.g., `acme`, `todo`). |
| `clientId` | `string` | Yes | Registered client identifier. |
| `clientSecret` | `string` | No | Client secret (required for confidential `AddServerSideWebApplication`). |
| `appUrl` | `string` | No | Canonical origin of the client application (e.g. `https://app.example.com`). Resolves issues behind reverse proxies. |
| `scopes` | `string[]` | No | OIDC scopes to request (defaults to `['openid', 'profile', 'email']`). Include `offline_access` for refresh tokens. |
| `par.enabled` | `boolean` | No | Whether to use RFC 9126 Pushed Authorization Requests (defaults to `true`). |
| `session.password` | `string` | Yes | 32+ character encryption secret for `iron-webcrypto`. |
| `session.userClaims` | `string[]` | No | Claims from the ID token / userinfo endpoint to serialize into the session cookie. |
| `storage` | `HuiaStorageAdapter` | No | Storage adapter for token cache (e.g., `RedisStorageAdapter`). |

---

## 2. Server Utilities API (`next-huia-oidc/server`)

### `createHuiaOidcHandler(config: HuiaOidcConfig)`
Returns an App Router Route Handler function (`(req: NextRequest) => Promise<Response>`) handling GET and POST for `/api/auth/[...huia]`.

### `getUserSession(config?: HuiaOidcConfig): Promise<UserSession | null>`
Reads and unseals the incoming session cookie. Returns `null` if unauthenticated or invalid.

### `getAccessToken(config?: HuiaOidcConfig): Promise<string | null>`
Retrieves the active access token from the session. Automatically invokes token refresh if expired.

---

## 3. Client Hook API (`next-huia-oidc/client`)

### `useUserSession()`
React hook providing:
- `user`: Object containing claims (e.g. `sub`, `name`, `email`, `roles`).
- `loggedIn`: Boolean indicating if the user has an active session.
- `isLoading`: Boolean indicating if the initial session check is running.
- `login(params?: { locale?: string; returnTo?: string })`: Redirects browser to initiate OIDC login.
- `clear()`: Initiates logout and clears the local session cookie.
