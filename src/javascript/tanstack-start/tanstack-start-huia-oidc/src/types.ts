import type {
  UserClaims,
  UserSession,
  TokenRecord,
  AuthStateRecord,
  CookiePayload,
  HuiaStorageAdapter,
  SessionOptions,
  CookieOptions,
} from 'huia-auth-core'
import type { OptionalFetcher } from '@tanstack/react-start'

export type { UserClaims, UserSession, TokenRecord, AuthStateRecord, CookiePayload, HuiaStorageAdapter, OptionalFetcher }

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
   * This app's own canonical origin, e.g. `http://localhost:3070` or `https://todo-tanstack.dev.localhost:3070`.
   * Used to build every absolute URL the handler issues on its own behalf.
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
    stateless: boolean
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

export interface HuiaVitePluginOptions {
  /**
   * Path to the route file to scaffold if missing. Defaults to 'src/routes/api/auth/$.ts'.
   */
  routeFile?: string
  /**
   * Path to the auth config file imported by the route file. Defaults to '@/auth.config'.
   */
  authConfigImportPath?: string
  /**
   * If true, suppresses automatic creation of the auth route file.
   */
  disableScaffolding?: boolean
}
