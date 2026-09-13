import type {
  UserClaims,
  UserSession,
  TokenRecord,
  HuiaStorageAdapter,
  SessionOptions,
  CookieOptions,
} from 'huia-auth-core'

export type { UserClaims, UserSession, TokenRecord, HuiaStorageAdapter }

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
