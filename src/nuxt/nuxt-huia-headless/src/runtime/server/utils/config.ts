import { getRequestProtocol, type H3Event } from 'h3'
import { useRuntimeConfig } from 'nitropack/runtime'
import type { ResolvedAuthConfig } from './internal-types'

interface RawHuiaHeadlessAuth {
  baseUrl: string
  session: { name: string, password: string, maxAge: number, cookie: { sameSite?: string, secure?: boolean }, userClaims: string[] }
  storage: { base: string }
  refresh: { enabled: boolean, earlyRefreshSeconds: number, lock: { ttlMs: number, waitMs: number, pollMs: number } }
  cookie: { chunkSize: number, maxChunks: number }
  allowInsecureTls: boolean
}

let warnedNoPassword = false

export function resolveAuthConfig(event: H3Event): ResolvedAuthConfig {
  const raw = useRuntimeConfig(event).huiaHeadless as unknown as RawHuiaHeadlessAuth

  const secure = raw.session.cookie?.secure ?? getRequestProtocol(event) === 'https'

  if (!raw.session.password && !warnedNoPassword) {
    warnedNoPassword = true
    console.warn('[huia-headless-auth] no session password — set NUXT_HUIA_HEADLESS_SESSION_PASSWORD (>= 32 chars)')
  }

  const configuredName = raw.session.name || '__Host-huia_headless_sess'
  // `__Host-` / `__Secure-` prefixed cookies are rejected by the browser unless `Secure` is set,
  // so over plain http (dev) the session cookie uses the bare name.
  const baseName = secure ? configuredName : configuredName.replace(/^__Host-/, '').replace(/^__Secure-/, '')

  return {
    baseUrl: raw.baseUrl.replace(/\/+$/, ''),
    secure,
    storageBase: raw.storage?.base || 'huia-headless-auth',
    session: {
      name: baseName,
      password: raw.session.password,
      maxAge: raw.session.maxAge || 60 * 60 * 24 * 7,
      userClaims: raw.session.userClaims ?? [],
    },
    cookie: {
      chunkSize: raw.cookie?.chunkSize || 3800,
      maxChunks: raw.cookie?.maxChunks || 8,
    },
    refresh: {
      enabled: raw.refresh?.enabled ?? true,
      earlyRefreshSeconds: raw.refresh?.earlyRefreshSeconds ?? 60,
      lock: {
        ttlMs: raw.refresh?.lock?.ttlMs ?? 10_000,
        waitMs: raw.refresh?.lock?.waitMs ?? 8_000,
        pollMs: raw.refresh?.lock?.pollMs ?? 150,
      },
    },
    allowInsecureTls: raw.allowInsecureTls ?? false,
  }
}
