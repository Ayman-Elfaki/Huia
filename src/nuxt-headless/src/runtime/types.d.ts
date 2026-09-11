import type { UserSession, UserSessionRequired, UserClaims, HuiaTokenResponse } from './types'

declare module '#huia-headless' {
  export type { UserSession, UserSessionRequired, UserClaims, HuiaTokenResponse }
}

declare module 'h3' {
  interface H3EventContext {
    huiaHeadless?: UserSession
  }
}

declare module '@nuxt/schema' {
  interface RuntimeConfig {
    huiaHeadless: {
      huia: { baseUrl: string, tenant: string }
      session: {
        name: string
        password: string
        maxAge: number
        cookie: Record<string, unknown>
        userClaims: string[]
      }
      storageBase: string
      refresh: {
        enabled: boolean
        earlyRefreshSeconds: number
      }
      cookie: { chunkSize: number, maxChunks: number }
      routes: Record<string, string>
    }
  }
  interface PublicRuntimeConfig {
    huiaHeadless: Record<string, string>
  }
}

export {}
