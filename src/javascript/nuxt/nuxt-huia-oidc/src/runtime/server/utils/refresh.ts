import * as oidc from 'openid-client'
import { createError, type H3Event } from 'h3'
import { randomUUID } from 'uncrypto'
import { resolveAuthConfig } from './config'
import { getOidcConfig, isInvalidGrant } from './oidc'
import {
  getTokenRecord,
  setTokenRecord,
  deleteTokenRecord,
  getLock,
  setLock,
  deleteLock,
  hasLock,
} from './storage'
import type { ResolvedAuthConfig, TokenRecord } from './internal-types'

export class RefreshTokenExpiredError extends Error {
  constructor(message = 'refresh_token_expired') {
    super(message)
    this.name = 'RefreshTokenExpiredError'
  }
}

const inflight = new Map<string, Promise<TokenRecord>>()
const sleep = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, ms))

export function needsRefresh(record: TokenRecord, cfg: ResolvedAuthConfig): boolean {
  return record.accessTokenExpiresAt - Date.now() < cfg.refresh.earlyRefreshSeconds * 1000
}

export async function ensureFreshTokens(event: H3Event, record: TokenRecord): Promise<TokenRecord> {
  const cfg = resolveAuthConfig(event)
  if (!needsRefresh(record, cfg)) return record

  const sid = record.sid
  const existing = inflight.get(sid)
  if (existing) return existing

  const p = guardedRefresh(cfg, sid, record)
  inflight.set(sid, p)
  try {
    return await p
  }
  finally {
    inflight.delete(sid)
  }
}

async function guardedRefresh(cfg: ResolvedAuthConfig, sid: string, record: TokenRecord): Promise<TokenRecord> {
  const lockId = randomUUID()
  const acquired = await tryAcquire(cfg, sid, lockId)

  if (acquired) {
    try {
      const fresh = await performRefresh(cfg, record)
      await setTokenRecord(cfg, fresh)
      return fresh
    }
    catch (err) {
      if (err instanceof RefreshTokenExpiredError || isInvalidGrant(err)) {
        await deleteTokenRecord(cfg, sid).catch(() => {})
        throw new RefreshTokenExpiredError()
      }
      throw err
    }
    finally {
      const held = await getLock(cfg, sid)
      if (held?.lockId === lockId) await deleteLock(cfg, sid).catch(() => {})
    }
  }

  // Lost the race — wait for the winner, then reuse its record.
  const deadline = Date.now() + cfg.refresh.lock.waitMs
  while (Date.now() < deadline) {
    await sleep(cfg.refresh.lock.pollMs)
    if (!(await hasLock(cfg, sid))) {
      const latest = await getTokenRecord(cfg, sid)
      if (latest === null) throw new RefreshTokenExpiredError('cleared_by_winner')
      if (!needsRefresh(latest, cfg)) return latest
      return guardedRefresh(cfg, sid, latest)
    }
  }

  if (await stealIfStale(cfg, sid)) {
    return guardedRefresh(cfg, sid, (await getTokenRecord(cfg, sid)) ?? record)
  }
  throw createError({ statusCode: 503, statusMessage: 'token_refresh_timeout' })
}

async function tryAcquire(cfg: ResolvedAuthConfig, sid: string, lockId: string): Promise<boolean> {
  const cur = await getLock(cfg, sid)
  if (cur && Date.now() - cur.acquiredAt < cfg.refresh.lock.ttlMs) return false
  await setLock(cfg, sid, { lockId, acquiredAt: Date.now() })
  // Re-read to shrink (not eliminate) the TOCTOU window on non-atomic drivers.
  return (await getLock(cfg, sid))?.lockId === lockId
}

async function stealIfStale(cfg: ResolvedAuthConfig, sid: string): Promise<boolean> {
  const cur = await getLock(cfg, sid)
  if (!cur || Date.now() - cur.acquiredAt >= cfg.refresh.lock.ttlMs) {
    await deleteLock(cfg, sid).catch(() => {})
    return true
  }
  return false
}

async function performRefresh(cfg: ResolvedAuthConfig, record: TokenRecord): Promise<TokenRecord> {
  if (!record.refreshToken) throw new RefreshTokenExpiredError('no_refresh_token')

  const config = await getOidcConfig(cfg.issuer)
  const res = await oidc.refreshTokenGrant(config, record.refreshToken, { scope: record.scope })
  const now = Date.now()
  const refreshExpiresIn = typeof res.refresh_expires_in === 'number' ? res.refresh_expires_in : undefined

  return {
    ...record,
    accessToken: res.access_token,
    refreshToken: res.refresh_token ?? record.refreshToken,
    idToken: res.id_token ?? record.idToken,
    claims: res.id_token ? (res.claims() ?? record.claims) : record.claims,
    scope: res.scope ?? record.scope,
    accessTokenExpiresAt: now + (Number(res.expires_in) || 300) * 1000,
    refreshTokenExpiresAt: refreshExpiresIn ? now + refreshExpiresIn * 1000 : record.refreshTokenExpiresAt,
    updatedAt: now,
  }
}
