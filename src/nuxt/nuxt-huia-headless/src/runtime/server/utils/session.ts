import { createError, type H3Event } from 'h3'
import { resolveAuthConfig } from './config'
import { readSessionCookie, writeSessionCookie, clearSessionCookies } from './cookie'
import { getTokenRecord, setTokenRecord, deleteTokenRecord, deleteLock, newSessionId } from './storage'
import { ensureFreshTokens, RefreshTokenExpiredError, needsRefresh } from './refresh'
import { pickUserClaims } from './tokens'
import type { UserSession, UserSessionRequired } from '../../types'
import type { TokenRecord, BackendTokenResponse, BackendMeResponse } from './internal-types'

const CTX = 'huiaHeadless' as const

function memo(event: H3Event, session: UserSession): UserSession {
  ;(event.context as Record<string, unknown>)[CTX] = session
  return session
}

export async function getUserSession(event: H3Event): Promise<UserSession> {
  const cached = (event.context as Record<string, unknown>)[CTX] as UserSession | undefined
  if (cached) return cached

  const cfg = resolveAuthConfig(event)
  const empty: UserSession = {}

  const payload = await readSessionCookie(event, cfg)
  if (!payload) return memo(event, empty)

  let record = await getTokenRecord(cfg, payload.sid)
  if (!record) {
    // Storage evicted the tokens (TTL, flush, logout elsewhere) — the cookie is meaningless.
    await clearSessionCookies(event, cfg)
    return memo(event, empty)
  }

  if (cfg.refresh.enabled && needsRefresh(record, cfg)) {
    try {
      record = await ensureFreshTokens(event, record)
    }
    catch (err) {
      if (err instanceof RefreshTokenExpiredError) {
        await clearUserSession(event)
        return memo(event, empty)
      }
      throw err
    }
  }

  return memo(event, { user: payload.user, loggedIn: true, expiresAt: record.accessTokenExpiresAt })
}

/** Establishes a session from a fresh token pair + the backend's `identity/me` response. */
export async function setUserSession(
  event: H3Event,
  tokens: BackendTokenResponse,
  me: BackendMeResponse,
): Promise<UserSession> {
  const cfg = resolveAuthConfig(event)

  const sid = newSessionId()
  const now = Date.now()
  const claims = pickUserClaims(me, cfg.session.userClaims)
  const record: TokenRecord = {
    sid,
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    tokenType: tokens.tokenType || 'Bearer',
    claims,
    accessTokenExpiresAt: now + (tokens.expiresIn || 300) * 1000,
    createdAt: now,
    updatedAt: now,
  }
  await setTokenRecord(cfg, record)

  await writeSessionCookie(event, cfg, {
    sid,
    user: claims,
    exp: now + cfg.session.maxAge * 1000,
  })
  return memo(event, { user: claims, loggedIn: true, expiresAt: record.accessTokenExpiresAt })
}

export async function clearUserSession(event: H3Event): Promise<void> {
  const cfg = resolveAuthConfig(event)
  const payload = await readSessionCookie(event, cfg)
  if (payload) {
    await deleteTokenRecord(cfg, payload.sid).catch(() => {})
    await deleteLock(cfg, payload.sid).catch(() => {})
  }
  await clearSessionCookies(event, cfg)
  memo(event, {})
}

export async function requireUserSession(event: H3Event): Promise<UserSessionRequired> {
  const session = await getUserSession(event)
  if (!session.loggedIn || !session.user) {
    throw createError({ statusCode: 401, statusMessage: 'Unauthorized', data: { code: 'auth_required' } })
  }
  return session as UserSessionRequired
}

/** Server-only. A valid (refreshed if needed) access token for calling upstream APIs. */
export async function getAccessToken(event: H3Event): Promise<string | null> {
  const cfg = resolveAuthConfig(event)
  const payload = await readSessionCookie(event, cfg)
  if (!payload) return null

  let record = await getTokenRecord(cfg, payload.sid)
  if (!record) return null

  if (cfg.refresh.enabled && needsRefresh(record, cfg)) {
    try {
      record = await ensureFreshTokens(event, record)
    }
    catch (err) {
      if (err instanceof RefreshTokenExpiredError) {
        await clearUserSession(event)
        return null
      }
      throw err
    }
  }
  return record.accessToken
}
