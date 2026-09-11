import type { UserSession, UserSessionRequired, SecureSessionData, UserClaims } from './types'

declare module '#huia-headless-auth' {
  export type { UserSession, UserSessionRequired, SecureSessionData, UserClaims }
}

declare module 'h3' {
  interface H3EventContext {
    /** Per-request memo of the resolved session (no tokens). */
    huiaHeadless?: UserSession
  }
}

declare module 'nitropack' {
  interface NitroRuntimeHooks {
    'huia-headless-auth:session:updated': (session: UserSession, event: import('h3').H3Event) => void
    'huia-headless-auth:session:cleared': (event: import('h3').H3Event) => void
  }
}

declare module '@nuxt/schema' {
  interface RuntimeConfig {
    huiaHeadless: {
      baseUrl: string
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
      allowInsecureTls: boolean
    }
  }
  interface PublicRuntimeConfig {
    huiaHeadless: {
      registerPath: string
      loginPath: string
      logoutPath: string
      sessionPath: string
      loginPage: string
      middlewareExclude: string[]
    }
  }
}

export {}
