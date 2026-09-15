import type {
  UserClaims,
  UserSession,
  TokenRecord,
  AuthStateRecord,
  HuiaStorageAdapter,
  SessionOptions,
  CookieOptions,
} from 'huia-auth-core'

export type { UserClaims, UserSession, TokenRecord, AuthStateRecord, HuiaStorageAdapter }

export interface HuiaOidcRoutes {
  login: string
  callback: string
  logout: string
  session: string
  error: string
}

export interface HuiaOidcConfig {
  /** The full issuer URL, e.g. https://id.huia.local/master or https://id.huia.local/todo */
  issuer?: string
  /** Base URL of the Huia Identity Server (used with `tenant` to build `issuer`) */
  baseUrl?: string
  /** Tenant name, e.g. "todo" or "master" */
  tenant?: string
  /** Client ID registered on the Identity Server */
  clientId: string
  /** Client Secret (if confidential client) */
  clientSecret?: string
  /** OAuth Redirect URI (defaults to /api/auth/callback) */
  redirectUri?: string
  /**
   * This app's own canonical origin, e.g. `http://todo-next.dev.localhost:3050`. Used to build every
   * absolute URL the handler issues on its own behalf — a relative `redirectUri`, the post-login
   * redirect, and the post-logout/error redirects — instead of the incoming request's `url.origin`.
   * Next.js's `NextRequest.url` is derived from how the server was started (`next dev`/`next start`,
   * defaulting to "localhost"), not from the request's actual `Host` header, so `url.origin` is wrong
   * whenever the app is reached under a different hostname (a reverse proxy, a `*.localhost` alias, a
   * container's service name). Falls back to `url.origin` when unset, which is correct only when the
   * app's configured listen address and its externally-reachable hostname happen to match.
   */
  appUrl?: string
  /** Requested OAuth scopes */
  scopes?: string[]
  /** Pushed Authorization Requests (RFC 9126) configuration */
  par?: {
    enabled?: boolean
    required?: boolean
  }
  /** Query parameters forwarded to /connect/authorize (e.g. ['ui_locales']) */
  allowedAuthParams?: string[]
  /** Session configuration (cookie encryption) */
  session: SessionOptions
  /** Cookie options */
  cookie?: CookieOptions
  /** Custom route paths (relative to app root or /api/auth) */
  routes?: Partial<HuiaOidcRoutes>
  /** Custom storage adapter for tokens (defaults to in-memory) */
  storage?: HuiaStorageAdapter
  /** Allow dev HTTPS self-signed certificates */
  allowInsecureTls?: boolean
}

export interface ResolvedHuiaOidcConfig {
  issuer: string
  clientId: string
  clientSecret?: string
  redirectUri: string
  appUrl?: string
  scopes: string[]
  par: {
    enabled: boolean
    required: boolean
  }
  allowedAuthParams: string[]
  session: {
    name: string
    password: string
    maxAge: number
    userClaims: string[]
  }
  cookie: {
    chunkSize: number
    maxChunks: number
    secure: boolean
    sameSite: 'lax' | 'strict' | 'none'
    path: string
  }
  routes: HuiaOidcRoutes
  storage: HuiaStorageAdapter
  allowInsecureTls: boolean
}
