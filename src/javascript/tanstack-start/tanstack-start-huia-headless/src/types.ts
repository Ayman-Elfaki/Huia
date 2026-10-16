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
import type { OptionalFetcher } from '@tanstack/react-start'

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
  OptionalFetcher,
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
   * This app's own canonical origin, e.g. `http://localhost:3080`.
   * Used to build the external-login callback URL.
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
    stateless: boolean
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
  | { ok: true; requiresProfile: true; flowId: string; maskedPhoneNumber?: string; email?: string | null; firstName?: string | null; lastName?: string | null }
  | { ok: false; requiresProfile: false; error: unknown }

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
