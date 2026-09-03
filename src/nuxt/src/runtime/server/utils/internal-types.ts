import type { UserClaims } from '../../types'

/** Minimal payload sealed into the session cookie(s). Never carries a token. */
export interface CookiePayload {
  /** Opaque session id — the key into Nitro Storage. */
  sid: string
  /** Whitelisted display claims. */
  user: UserClaims
  /** ms epoch — cookie/session hard expiry. */
  exp: number
}

/** Server-only token record, keyed by `sid` in Nitro Storage. Never serialised to the client. */
export interface TokenRecord {
  sid: string
  accessToken: string
  refreshToken?: string
  idToken?: string
  tokenType: string
  scope: string
  /** Full id_token claim set. */
  claims: Record<string, unknown>
  accessTokenExpiresAt: number
  refreshTokenExpiresAt?: number
  createdAt: number
  updatedAt: number
}

/** Server-only flow state for one in-progress Authorization Code request, keyed by `state`. */
export interface AuthStateRecord {
  state: string
  nonce: string
  codeVerifier: string
  redirectUri: string
  returnTo: string
  createdAt: number
}

/** Soft-lock record for {@link TokenRecord} refresh, keyed by `sid`. */
export interface LockRecord {
  lockId: string
  acquiredAt: number
}

/** Fully-resolved per-request configuration (see `config.ts#resolveAuthConfig`). */
export interface ResolvedAuthConfig {
  issuer: string
  clientId: string
  clientSecret: string
  redirectUri: string
  scopes: string[]
  allowedAuthParams: string[]
  par: { enabled: boolean, required: boolean }
  secure: boolean
  storageBase: string
  session: {
    name: string
    password: string
    maxAge: number
    userClaims: string[]
  }
  cookie: { chunkSize: number, maxChunks: number }
  refresh: {
    enabled: boolean
    earlyRefreshSeconds: number
    lock: { ttlMs: number, waitMs: number, pollMs: number }
  }
  oauthCookieName: string
  errorPath: string
  logout: { rpInitiated: boolean }
  allowInsecureTls: boolean
}
