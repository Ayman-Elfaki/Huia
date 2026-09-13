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
