import { type H3Event, createError } from 'h3'
import type { ResolvedHeadlessConfig, TokenRecord } from './internal-types'
import { readSessionCookie, writeSessionCookie } from './cookie'
import { getTokenRecord, setTokenRecord, deleteTokenRecord, getLock, setLock, deleteLock } from './storage'
import { createTokenRecordFromResponse, extractUserClaims } from './tokens'
import type { HuiaTokenResponse } from '../../types'

export async function performRefresh(
  cfg: ResolvedHeadlessConfig,
  sid: string,
  refreshToken: string
): Promise<TokenRecord> {
  const url = `${cfg.huia.baseUrl}/${cfg.huia.tenant}/identity/refresh`
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  })

  if (!res.ok) {
    await deleteTokenRecord(cfg, sid)
    throw createError({
      statusCode: 401,
      statusMessage: 'Refresh token invalid or expired',
    })
  }

  const data = (await res.json()) as HuiaTokenResponse
  const record = createTokenRecordFromResponse(data, sid)
  await setTokenRecord(cfg, record)
  return record
}

export async function ensureFreshTokens(
  event: H3Event,
  cfg: ResolvedHeadlessConfig
): Promise<TokenRecord | null> {
  const cookie = await readSessionCookie(event, cfg)
  if (!cookie?.sid) return null

  const record = await getTokenRecord(cfg, cookie.sid)
  if (!record) return null

  const now = Date.now()
  const earlyMs = cfg.refresh.earlyRefreshSeconds * 1000
  const isExpiringSoon = record.accessTokenExpiresAt - now <= earlyMs

  if (!isExpiringSoon || !cfg.refresh.enabled) {
    return record
  }

  // Handle refresh concurrency with lock
  const lock = await getLock(cfg, cookie.sid)
  if (lock && now - lock.acquiredAt < cfg.refresh.lock.ttlMs) {
    // Another request is refreshing; wait and re-fetch from storage
    const start = Date.now()
    while (Date.now() - start < cfg.refresh.lock.waitMs) {
      await new Promise((r) => setTimeout(r, cfg.refresh.lock.pollMs))
      const fresh = await getTokenRecord(cfg, cookie.sid)
      if (fresh && fresh.accessTokenExpiresAt - Date.now() > earlyMs) {
        return fresh
      }
    }
  }

  await setLock(cfg, cookie.sid, { owner: cookie.sid, acquiredAt: now })
  try {
    const updated = await performRefresh(cfg, cookie.sid, record.refreshToken)
    const user = extractUserClaims(updated.claims, cfg.session.userClaims)
    await writeSessionCookie(event, cfg, {
      sid: cookie.sid,
      user,
      expiresAt: updated.accessTokenExpiresAt,
    })
    return updated
  }
  finally {
    await deleteLock(cfg, cookie.sid)
  }
}
