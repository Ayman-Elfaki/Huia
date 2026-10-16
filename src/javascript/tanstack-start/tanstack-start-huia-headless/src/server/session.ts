import {
  ensureFreshTokenRecord,
  needsRefresh,
  HuiaHeadlessClient,
  type CookiePayload,
  type UserSession,
  type TokenRecord,
} from 'huia-auth-core'
import { resolveHeadlessConfig } from './config.js'
import { parseCookieHeader, readSessionFromCookies } from './cookies.js'
import type { HuiaHeadlessConfig, ResolvedHuiaHeadlessConfig } from '../types.js'

function getCookieGetter(request?: Request): (name: string) => string | undefined {
  if (request) {
    const cookies = parseCookieHeader(request.headers.get('cookie'))
    return (name: string) => cookies[name]
  }

  return (name: string) => {
    try {
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
 * Server helper to get current session in headless mode.
 */
export async function getHuiaHeadlessSession(
  configInput: HuiaHeadlessConfig | ResolvedHuiaHeadlessConfig,
  request?: Request,
): Promise<UserSession> {
  try {
    const cfg = resolveHeadlessConfig(configInput)
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
 * Server helper to get a fresh bearer access token in headless mode.
 */
export async function getHeadlessAccessToken(
  configInput: HuiaHeadlessConfig | ResolvedHuiaHeadlessConfig,
  request?: Request,
): Promise<string | null> {
  const cfg = resolveHeadlessConfig(configInput)
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
        const client = new HuiaHeadlessClient({
          baseUrl: cfg.baseUrl,
          allowInsecureTls: cfg.allowInsecureTls,
        })
        const res = await client.refresh(record.refreshToken)
        const now = Date.now()
        const expiresIn = res.expiresIn || 300
        const updatedRecord: TokenRecord = {
          ...record,
          accessToken: res.accessToken,
          refreshToken: res.refreshToken ?? record.refreshToken,
          accessTokenExpiresAt: now + expiresIn * 1000,
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
      const client = new HuiaHeadlessClient({
        baseUrl: cfg.baseUrl,
        allowInsecureTls: cfg.allowInsecureTls,
      })
      const res = await client.refresh(rec.refreshToken)
      const now = Date.now()
      const expiresIn = res.expiresIn || 300

      return {
        ...rec,
        accessToken: res.accessToken,
        refreshToken: res.refreshToken ?? rec.refreshToken,
        accessTokenExpiresAt: now + expiresIn * 1000,
        updatedAt: now,
      }
    },
  )

  return freshRecord.accessToken
}

export { getHeadlessAccessToken as getAccessToken, getHuiaHeadlessSession as getHuiaSession }
