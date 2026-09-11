import { type H3Event, createError } from 'h3'
import type { UserSession, UserSessionRequired, HuiaTokenResponse } from '../../types'
import { useHeadlessConfig } from './config'
import { readSessionCookie, writeSessionCookie, clearSessionCookie } from './cookie'
import { setTokenRecord, deleteTokenRecord } from './storage'
import { createTokenRecordFromResponse, extractUserClaims } from './tokens'
import { ensureFreshTokens } from './refresh'

export async function getUserSession(event: H3Event): Promise<UserSession> {
  const cfg = useHeadlessConfig(event)
  const freshRecord = await ensureFreshTokens(event, cfg)

  if (!freshRecord) {
    clearSessionCookie(event, cfg)
    return { loggedIn: false }
  }

  const user = extractUserClaims(freshRecord.claims, cfg.session.userClaims)
  return {
    user,
    loggedIn: true,
    expiresAt: freshRecord.accessTokenExpiresAt,
  }
}

export async function requireUserSession(event: H3Event): Promise<UserSessionRequired> {
  const session = await getUserSession(event)
  if (!session.loggedIn || !session.user) {
    throw createError({
      statusCode: 401,
      statusMessage: 'Unauthorized: Session required',
    })
  }
  return session as UserSessionRequired
}

export async function setUserSession(event: H3Event, tokens: HuiaTokenResponse): Promise<UserSession> {
  const cfg = useHeadlessConfig(event)
  const record = createTokenRecordFromResponse(tokens)

  await setTokenRecord(cfg, record)

  const user = extractUserClaims(record.claims, cfg.session.userClaims)
  await writeSessionCookie(event, cfg, {
    sid: record.sid,
    user,
    expiresAt: record.accessTokenExpiresAt,
  })

  return {
    user,
    loggedIn: true,
    expiresAt: record.accessTokenExpiresAt,
  }
}

export async function clearUserSession(event: H3Event): Promise<void> {
  const cfg = useHeadlessConfig(event)
  const cookie = await readSessionCookie(event, cfg)
  if (cookie?.sid) {
    await deleteTokenRecord(cfg, cookie.sid)
  }
  clearSessionCookie(event, cfg)
}

export async function getAccessToken(event: H3Event): Promise<string | null> {
  const cfg = useHeadlessConfig(event)
  const fresh = await ensureFreshTokens(event, cfg)
  return fresh?.accessToken ?? null
}
