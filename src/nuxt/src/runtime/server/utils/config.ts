import { getRequestURL, getRequestProtocol, type H3Event } from 'h3'
import { useRuntimeConfig } from 'nitropack/runtime'
import type { ResolvedAuthConfig } from './internal-types'

interface RawHuiaAuth {
  clientId: string
  clientSecret: string
  issuer: string
  huia: { baseUrl: string, tenant: string }
  redirectUrl: string
  scopes: string[]
  allowedAuthParams: string[]
  par: { enabled: boolean, required: boolean }
  session: { name: string, password: string, maxAge: number, cookie: { sameSite?: string, secure?: boolean }, userClaims: string[] }
  storage: { base: string }
  refresh: { enabled: boolean, earlyRefreshSeconds: number, lock: { ttlMs: number, waitMs: number, pollMs: number } }
  cookie: { chunkSize: number, maxChunks: number }
  routes: { login: string, callback: string, logout: string, session: string, error: string }
  allowInsecureTls: boolean
  logout?: { rpInitiated?: boolean }
}

/** `{baseUrl}/{tenant}` (or an explicit `issuer`), with any trailing slash stripped. */
export function resolveIssuer(raw: Pick<RawHuiaAuth, 'issuer' | 'huia'>): string {
  if (raw.issuer) return raw.issuer.replace(/\/+$/, '')
  const base = raw.huia.baseUrl.replace(/\/+$/, '')
  const tenant = raw.huia.tenant.replace(/^\/+|\/+$/g, '')
  return tenant ? `${base}/${tenant}` : base
}

export function discoveryCacheKey(issuer: string): string {
  return `discovery:${issuer.replace(/[^a-z0-9]+/gi, '_')}`
}

let warnedNoPassword = false

export function resolveAuthConfig(event: H3Event): ResolvedAuthConfig {
  const raw = useRuntimeConfig(event).huiaAuth as unknown as RawHuiaAuth

  const url = getRequestURL(event)
  const secure = raw.session.cookie?.secure ?? getRequestProtocol(event) === 'https'
  const redirectUri = new URL(raw.redirectUrl, url.origin).href

  if (!raw.session.password && !warnedNoPassword) {
    warnedNoPassword = true
    console.warn('[huia-auth] no session password — set NUXT_HUIA_AUTH_SESSION_PASSWORD (>= 32 chars)')
  }

  const configuredName = raw.session.name || '__Host-huia_sess'
  // `__Host-` / `__Secure-` prefixed cookies are rejected by the browser unless `Secure` is set,
  // so over plain http (dev) both the session and the transient oauth-state cookie use the bare name.
  const baseName = secure ? configuredName : configuredName.replace(/^__Host-/, '').replace(/^__Secure-/, '')
  const oauthCookieName = baseName.endsWith('_sess')
    ? `${baseName.slice(0, -'_sess'.length)}_oauth`
    : `${baseName}_oauth`

  return {
    issuer: resolveIssuer(raw),
    clientId: raw.clientId,
    clientSecret: raw.clientSecret,
    redirectUri,
    scopes: raw.scopes,
    allowedAuthParams: raw.allowedAuthParams ?? [],
    par: { enabled: raw.par?.enabled ?? true, required: raw.par?.required ?? false },
    secure,
    storageBase: raw.storage?.base || 'huia-auth',
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
    oauthCookieName,
    errorPath: raw.routes?.error || '/',
    logout: { rpInitiated: raw.logout?.rpInitiated ?? true },
    allowInsecureTls: raw.allowInsecureTls ?? false,
  }
}
