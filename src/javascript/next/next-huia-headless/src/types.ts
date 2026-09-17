import type {
  UserClaims,
  UserSession,
  TokenRecord,
  CookiePayload,
  HuiaStorageAdapter,
  HeadlessAdminUser,
  HeadlessAdminRole,
  HeadlessAdminUsersPage,
  CreateHeadlessAdminUserRequest,
  UpdateHeadlessAdminUserRequest,
  SessionOptions,
  CookieOptions,
} from 'huia-auth-core'

export type {
  UserClaims,
  UserSession,
  TokenRecord,
  CookiePayload,
  HuiaStorageAdapter,
  HeadlessAdminUser,
  HeadlessAdminRole,
  HeadlessAdminUsersPage,
  CreateHeadlessAdminUserRequest,
  UpdateHeadlessAdminUserRequest,
}

export interface HuiaHeadlessRoutes {
  login: string
  register: string
  logout: string
  session: string
  refresh: string
  phoneStart: string
  phoneVerify: string
  phoneCompleteProfile: string
  externalLogin: string
  externalExchange: string
  externalCompleteProfile: string
}

export interface HuiaHeadlessConfig {
  /** Base URL of the Huia.Headless identity API (e.g. https://localhost:5341) */
  baseUrl: string
  /**
   * This app's own canonical origin, e.g. `http://shop-next.dev.localhost:3060`. Used to build the
   * external-login callback URL instead of the incoming request's `url.origin`. Next.js's
   * `NextRequest.url` is derived from how the server was started (`next dev`/`next start`, defaulting
   * to "localhost"), not from the request's actual `Host` header, so `url.origin` is wrong whenever the
   * app is reached under a different hostname (a reverse proxy, a `*.localhost` alias, a container's
   * service name). Falls back to `url.origin` when unset, which is correct only when the app's
   * configured listen address and its externally-reachable hostname happen to match.
   */
  appUrl?: string
  /** Session configuration (cookie encryption) */
  session: SessionOptions
  /** Cookie options */
  cookie?: CookieOptions
  /** Custom route paths (defaults to standard /api/auth/*) */
  routes?: Partial<HuiaHeadlessRoutes>
  /** Custom storage adapter for token records (defaults to in-memory) */
  storage?: HuiaStorageAdapter
  /** Allow dev HTTPS self-signed certificates */
  allowInsecureTls?: boolean
}

export interface ResolvedHuiaHeadlessConfig {
  baseUrl: string
  appUrl?: string
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
  routes: HuiaHeadlessRoutes
  storage: HuiaStorageAdapter
  allowInsecureTls: boolean
}

export interface HuiaLoginResult {
  ok: boolean
  user?: UserClaims
  error?: unknown
}

export type HuiaFlowResult =
  | { ok: true; requiresProfile: false; user: UserClaims }
  | { ok: true; requiresProfile: true; flowId: string; email?: string | null; firstName?: string | null; lastName?: string | null }
  | { ok: false; requiresProfile: false; error: unknown }
