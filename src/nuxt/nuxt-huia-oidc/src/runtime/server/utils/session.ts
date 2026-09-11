import { createError, type H3Event } from 'h3'
import { resolveAuthConfig } from './config'
import { readSessionCookie, writeSessionCookie, clearSessionCookies } from './cookie'
import { getTokenRecord, setTokenRecord, deleteTokenRecord, deleteLock, newSessionId } from './storage'
import { ensureFreshTokens, RefreshTokenExpiredError, needsRefresh } from './refresh'
import { pickUserClaims } from './tokens'
import type { UserSession, UserSessionRequired } from '../../types'
import type { TokenRecord } from './internal-types'
import type { TokenResponse } from './oidc'

const CTX = 'huiaAuth' as const

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

export async function setUserSession(
  event: H3Event,
  data: Partial<UserSession> & { tokens?: TokenResponse, claims?: Record<string, unknown> },
): Promise<UserSession> {
  const cfg = resolveAuthConfig(event)

  if (data.tokens && data.claims) {
    const sid = newSessionId()
    const now = Date.now()
    const record: TokenRecord = {
      sid,
      accessToken: data.tokens.access_token,
      refreshToken: data.tokens.refresh_token,
      idToken: data.tokens.id_token,
      tokenType: data.tokens.token_type ?? 'bearer',
      scope: data.tokens.scope ?? cfg.scopes.join(' '),
      claims: data.claims,
      accessTokenExpiresAt: now + (Number(data.tokens.expires_in) || 300) * 1000,
      refreshTokenExpiresAt: typeof data.tokens.refresh_expires_in === 'number'
        ? now + data.tokens.refresh_expires_in * 1000
        : undefined,
      createdAt: now,
      updatedAt: now,
    }
    await setTokenRecord(cfg, record)

    const user = pickUserClaims(data.claims, cfg.session.userClaims)
    await writeSessionCookie(event, cfg, {
      sid,
      user,
      exp: now + cfg.session.maxAge * 1000,
    })
    return memo(event, { user, loggedIn: true, expiresAt: record.accessTokenExpiresAt })
  }

  const current = await readSessionCookie(event, cfg)
  if (!current) throw createError({ statusCode: 401, statusMessage: 'No session to update' })
  const user = { ...current.user, ...(data.user ?? {}) }
  await writeSessionCookie(event, cfg, { ...current, user })
  const prev = (event.context as Record<string, unknown>)[CTX] as UserSession | undefined
  return memo(event, { user, loggedIn: true, expiresAt: prev?.expiresAt })
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

/** Server-only. The full token record (incl. id_token) — used by the logout handler. */
export async function getSecureTokenRecord(event: H3Event): Promise<TokenRecord | null> {
  const cfg = resolveAuthConfig(event)
  const payload = await readSessionCookie(event, cfg)
  return payload ? getTokenRecord(cfg, payload.sid) : null
}
