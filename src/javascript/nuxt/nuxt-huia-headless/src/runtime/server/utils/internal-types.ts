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
  tokenType: string
  claims: UserClaims
  accessTokenExpiresAt: number
  createdAt: number
  updatedAt: number
}

/** Soft-lock record for {@link TokenRecord} refresh, keyed by `sid`. */
export interface LockRecord {
  lockId: string
  acquiredAt: number
}

/** Fully-resolved per-request configuration (see `config.ts#resolveAuthConfig`). */
export interface ResolvedAuthConfig {
  baseUrl: string
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
  allowInsecureTls: boolean
  /** The app's own page that reads `?code=` after the external-provider callback and calls `exchange`. */
  externalCallbackPath: string
}

/** The bearer-token pair `Huia.Headless`'s `identity/login` / `identity/refresh` return. */
export interface BackendTokenResponse {
  tokenType: string
  accessToken: string
  expiresIn: number
  refreshToken: string
}

/** The shape `GET identity/me` returns. */
export interface BackendMeResponse {
  sub: string
  email: string | null
  emailConfirmed: boolean
  phoneNumber: string | null
  phoneNumberConfirmed: boolean
  firstName: string
  lastName: string
  roles: string[]
}

/** `POST identity/phone/start`'s response. */
export interface BackendPhoneStartResponse {
  flowId: string
}

/** `POST identity/phone/verify` or `.../complete-profile`'s response — either shape, never both. */
export type BackendPhoneVerifyResponse =
  | (BackendTokenResponse & { requiresProfile?: never })
  | { flowId: string, requiresProfile: true }

/** `POST identity/account/external/exchange` or `.../complete-profile`'s response — either shape. */
export type BackendExternalExchangeResponse =
  | (BackendTokenResponse & { requiresProfile?: never })
  | { code: string, requiresProfile: true, email: string | null, firstName: string | null, lastName: string | null }
