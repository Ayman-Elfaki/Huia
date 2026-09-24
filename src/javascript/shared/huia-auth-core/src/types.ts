/**
 * Shared type definitions for Huia authentication libraries.
 */

export interface UserClaims {
  sub?: string
  name?: string
  email?: string
  email_verified?: boolean
  preferred_username?: string
  given_name?: string
  family_name?: string
  phone_number?: string
  phone_number_verified?: boolean
  roles?: string[]
  [key: string]: unknown
}

export interface UserSession {
  user?: UserClaims | null
  loggedIn?: boolean
  expiresAt?: number
}

export interface TokenRecord {
  sid: string
  accessToken: string
  refreshToken?: string
  idToken?: string
  claims: UserClaims
  scope?: string
  accessTokenExpiresAt: number
  refreshTokenExpiresAt?: number
  updatedAt: number
}

export interface AuthStateRecord {
  state: string
  codeVerifier: string
  nonce: string
  returnTo?: string
  createdAt: number
}

export interface LockRecord {
  lockId: string
  acquiredAt: number
}

export interface CookiePayload {
  sid: string
  user: UserClaims
  expiresAt: number
  tokens?: TokenRecord
  stateless?: boolean
}

export interface SessionOptions {
  name?: string
  password: string
  maxAge?: number // seconds, default 7 days (604800)
  userClaims?: string[]
  /**
   * When true, all session data (including tokens) is encrypted and stored directly inside a
   * secure, HttpOnly cookie in the user's browser. The server does not keep track of who is logged in.
   */
  stateless?: boolean
}

export interface CookieOptions {
  chunkSize?: number // default 3800
  maxChunks?: number // default 4
  secure?: boolean
  sameSite?: 'lax' | 'strict' | 'none'
  path?: string
}

export interface HuiaStorageAdapter {
  getTokenRecord(sid: string): Promise<TokenRecord | null>
  setTokenRecord(sid: string, record: TokenRecord, ttlSeconds?: number): Promise<void>
  deleteTokenRecord(sid: string): Promise<void>

  getStateRecord?(state: string): Promise<AuthStateRecord | null>
  setStateRecord?(state: string, record: AuthStateRecord, ttlSeconds?: number): Promise<void>
  deleteStateRecord?(state: string): Promise<void>

  getLock(sid: string): Promise<LockRecord | null>
  setLock(sid: string, lock: LockRecord, ttlSeconds?: number): Promise<void>
  deleteLock(sid: string): Promise<void>
}

/* ── Headless API Types ── */

export interface BackendTokenResponse {
  tokenType: string
  accessToken: string
  expiresIn: number
  refreshToken: string
}

export interface BackendMeResponse {
  email: string
  isEmailConfirmed: boolean
  roles?: string[]
  firstName?: string
  lastName?: string
  phoneNumber?: string
  isPhoneNumberConfirmed?: boolean
  [key: string]: unknown
}

export interface BackendPhoneStartResponse {
  flowId: string
  expiresInSeconds: number
  status?: string
}

export interface BackendPhoneVerifyResponse {
  accessToken?: string
  refreshToken?: string
  expiresIn?: number
  requiresProfile?: boolean
  flowId?: string
  firstName?: string | null
  lastName?: string | null
}

export interface BackendExternalExchangeResponse {
  accessToken?: string
  refreshToken?: string
  expiresIn?: number
  requiresProfile?: boolean
  code?: string
  email?: string | null
  firstName?: string | null
  lastName?: string | null
}

export interface HeadlessAdminUser {
  id: string
  userName?: string | null
  email?: string | null
  emailConfirmed: boolean
  phoneNumber?: string | null
  phoneNumberConfirmed: boolean
  firstName: string
  lastName: string
  lockoutEnabled: boolean
  lockoutEnd?: string | null
  roles: string[]
}

export interface HeadlessAdminRole {
  id: string
  name: string
  origin: string
}

export interface HeadlessAdminUsersPage {
  data: HeadlessAdminUser[]
  totalCount: number
  page: number
  pageSize: number
  hasNext: boolean
  hasPrevious: boolean
}

export interface CreateHeadlessAdminUserRequest {
  email?: string
  password?: string
  phoneNumber?: string
  firstName?: string
  lastName?: string
  emailConfirmed?: boolean
  roles?: string[]
}

export interface UpdateHeadlessAdminUserRequest {
  firstName?: string
  lastName?: string
  email?: string
  phoneNumber?: string
}
