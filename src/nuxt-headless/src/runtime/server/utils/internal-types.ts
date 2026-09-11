import type { UserClaims } from '../../types'

export interface ResolvedHeadlessConfig {
  huia: {
    baseUrl: string
    tenant: string
  }
  session: {
    name: string
    password: string
    maxAge: number
    cookie: {
      sameSite: 'lax' | 'strict' | 'none'
      secure?: boolean
    }
    userClaims: string[]
  }
  storageBase: string
  refresh: {
    enabled: boolean
    earlyRefreshSeconds: number
    lock: {
      ttlMs: number
      waitMs: number
      pollMs: number
    }
  }
  cookie: {
    chunkSize: number
    maxChunks: number
  }
  routes: {
    register: string
    login: string
    logout: string
    refresh: string
    phoneStart: string
    phoneVerify: string
    phoneCompleteProfile: string
    session: string
  }
  secure: boolean
}

export interface CookiePayload {
  sid: string
  user: UserClaims
  expiresAt: number
}

export interface TokenRecord {
  sid: string
  accessToken: string
  refreshToken: string
  accessTokenExpiresAt: number
  refreshTokenExpiresAt?: number
  claims: Record<string, unknown>
}

export interface LockRecord {
  owner: string
  acquiredAt: number
}
