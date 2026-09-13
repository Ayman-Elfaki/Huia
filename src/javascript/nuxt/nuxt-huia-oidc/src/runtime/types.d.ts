import type { UserSession, UserSessionRequired, SecureSessionData, UserClaims } from './types'

declare module '#huia-auth' {
  export type { UserSession, UserSessionRequired, SecureSessionData, UserClaims }
}

declare module 'h3' {
  interface H3EventContext {
    /** Per-request memo of the resolved session (no tokens). */
    huia?: UserSession
  }
}

declare module 'nitropack' {
  interface NitroRuntimeHooks {
    'huia-auth:session:updated': (session: UserSession, event: import('h3').H3Event) => void
    'huia-auth:session:cleared': (event: import('h3').H3Event) => void
  }
}

declare module '@nuxt/schema' {
  interface RuntimeConfig {
    huia: {
      clientId: string
      clientSecret: string
      issuer: string
      baseUrl: string
      tenant: string
      redirectUrl: string
      scopes: string[]
      allowedAuthParams: string[]
      par: { enabled: boolean, required: boolean }
      session: {
        name: string
        password: string
        maxAge: number
        cookie: Record<string, unknown>
        userClaims: string[]
      }
      storage: { base: string }
      refresh: {
        enabled: boolean
        earlyRefreshSeconds: number
        lock: { ttlMs: number, waitMs: number, pollMs: number }
      }
      cookie: { chunkSize: number, maxChunks: number }
      routes: { login: string, callback: string, logout: string, session: string, error: string }
      allowInsecureTls: boolean
      logout?: { rpInitiated?: boolean }
    }
  }
  interface PublicRuntimeConfig {
    huia: { loginPath: string, logoutPath: string, sessionPath: string, middlewareExclude: string[] }
  }
}

export {}
