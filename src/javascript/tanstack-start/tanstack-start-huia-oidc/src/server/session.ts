import {
  defaultOidcHelper,
  ensureFreshTokenRecord,
  needsRefresh,
  type CookiePayload,
  type UserSession,
  type TokenRecord,
} from 'huia-auth-core'
import { resolveOidcConfig } from './config.js'
import { parseCookieHeader, readSessionFromCookies } from './cookies.js'
import type { HuiaOidcConfig, ResolvedHuiaOidcConfig } from '../types.js'

function getCookieGetter(request?: Request): (name: string) => string | undefined {
  if (request) {
    const cookies = parseCookieHeader(request.headers.get('cookie'))
    return (name: string) => cookies[name]
  }

  return (name: string) => {
    try {
      // Dynamic require or module import check for @tanstack/react-start/server
      // When running inside TanStack Start server execution context:
      const globalAny = globalThis as unknown as {
        __tanstack_start_getCookie?: (name: string) => string | undefined
      }
      if (typeof globalAny.__tanstack_start_getCookie === 'function') {
        return globalAny.__tanstack_start_getCookie(name)
      }
    }
    catch {
      // Ignore
    }
    return undefined
  }
}

/**
 * Server helper to get the current user session from an incoming Request or TanStack Start context.
 */
export async function getHuiaSession(
  configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig,
  request?: Request,
): Promise<UserSession> {
  try {
    const cfg = resolveOidcConfig(configInput)
    let cookieGetter = getCookieGetter(request)

    // Try reading via passed request or start context
    let payload = await readSessionFromCookies(cookieGetter, cfg)

    // Fallback: If not passed request, try importing getCookie from @tanstack/react-start/server
    if (!payload && !request) {
      try {
        const { getCookie } = await import('@tanstack/react-start/server')
        payload = await readSessionFromCookies(name => getCookie(name), cfg)
      }
      catch {
        // Not in TanStack Start server context
      }
    }

    if (!payload || !payload.user) {
      return { user: null, loggedIn: false }
    }

    return {
      user: payload.user,
      loggedIn: true,
      expiresAt: payload.expiresAt,
    }
  }
  catch {
    return { user: null, loggedIn: false }
  }
}

/**
 * Server helper to get a fresh bearer access token for upstream API requests.
 */
export async function getAccessToken(
  configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig,
  request?: Request,
): Promise<string | null> {
  const cfg = resolveOidcConfig(configInput)
  let cookieGetter = getCookieGetter(request)
  let payload = await readSessionFromCookies(cookieGetter, cfg)

  if (!payload && !request) {
    try {
      const { getCookie } = await import('@tanstack/react-start/server')
      payload = await readSessionFromCookies(name => getCookie(name), cfg)
    }
    catch {
      // Ignore
    }
  }

  if (!payload) return null

  if (payload.stateless || cfg.session.stateless) {
    const record = payload.tokens
    if (!record) return null
    if (needsRefresh(record.accessTokenExpiresAt)) {
      if (!record.refreshToken) return record.accessToken
      try {
        const oidcConfig = await defaultOidcHelper.getConfiguration(cfg)
        const res = await defaultOidcHelper.refreshTokens(oidcConfig, record.refreshToken, record.scope)
        const now = Date.now()
        const expiresIn = Number(res.expires_in) || 300
        const refreshExpiresIn = typeof res.refresh_expires_in === 'number' ? res.refresh_expires_in : undefined
        const updatedRecord: TokenRecord = {
          ...record,
          accessToken: res.access_token,
          refreshToken: res.refresh_token ?? record.refreshToken,
          idToken: res.id_token ?? record.idToken,
          claims: res.id_token ? ((res.claims() as never) ?? record.claims) : record.claims,
          accessTokenExpiresAt: now + expiresIn * 1000,
          refreshTokenExpiresAt: refreshExpiresIn ? now + refreshExpiresIn * 1000 : record.refreshTokenExpiresAt,
          updatedAt: now,
        }
        return updatedRecord.accessToken
      }
      catch {
        return record.accessToken
      }
    }
    return record.accessToken
  }

  if (!payload.sid) return null

  const record = await cfg.storage.getTokenRecord(payload.sid)
  if (!record) return null

  const freshRecord = await ensureFreshTokenRecord(
    cfg.storage,
    record,
    async (rec: TokenRecord) => {
      if (!rec.refreshToken) throw new Error('no_refresh_token')
      const oidcConfig = await defaultOidcHelper.getConfiguration(cfg)
      const res = await defaultOidcHelper.refreshTokens(oidcConfig, rec.refreshToken, rec.scope)
      const now = Date.now()

      return {
        ...rec,
        accessToken: res.access_token,
        refreshToken: res.refresh_token ?? rec.refreshToken,
        idToken: res.id_token ?? rec.idToken,
        claims: res.id_token ? ((res.claims() as never) ?? rec.claims) : rec.claims,
        accessTokenExpiresAt: now + (Number(res.expires_in) || 300) * 1000,
        updatedAt: now,
      }
    },
  )

  return freshRecord.accessToken
}
