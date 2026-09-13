import { randomUUID } from 'uncrypto'
import type { HuiaStorageAdapter, TokenRecord } from './types.js'
import { needsRefresh } from './tokens.js'

export class RefreshTokenExpiredError extends Error {
  constructor(message = 'refresh_token_expired') {
    super(message)
    this.name = 'RefreshTokenExpiredError'
  }
}

export interface RefreshLockOptions {
  ttlMs?: number // default 15000
  earlyRefreshSeconds?: number // default 30
  retryIntervalMs?: number // default 100
  maxWaitMs?: number // default 5000
}

const inflight = new Map<string, Promise<TokenRecord>>()
const sleep = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, ms))

/**
 * Ensures token record is fresh, using deduplicated in-memory promises and storage locking.
 */
export async function ensureFreshTokenRecord(
  storage: HuiaStorageAdapter,
  record: TokenRecord,
  refreshFn: (record: TokenRecord) => Promise<TokenRecord>,
  options: RefreshLockOptions = {},
): Promise<TokenRecord> {
  const earlyRefreshSeconds = options.earlyRefreshSeconds ?? 30
  if (!needsRefresh(record.accessTokenExpiresAt, earlyRefreshSeconds)) {
    return record
  }

  const sid = record.sid
  const existing = inflight.get(sid)
  if (existing) return existing

  const p = guardedRefresh(storage, sid, record, refreshFn, options)
  inflight.set(sid, p)
  try {
    return await p
  }
  finally {
    inflight.delete(sid)
  }
}

async function guardedRefresh(
  storage: HuiaStorageAdapter,
  sid: string,
  record: TokenRecord,
  refreshFn: (record: TokenRecord) => Promise<TokenRecord>,
  options: RefreshLockOptions,
): Promise<TokenRecord> {
  const lockId = randomUUID()
  const ttlMs = options.ttlMs ?? 15000
  const maxWaitMs = options.maxWaitMs ?? 5000
  const retryIntervalMs = options.retryIntervalMs ?? 100

  // 1. Try to acquire the lock
  if (await tryAcquireLock(storage, sid, lockId, ttlMs)) {
    try {
      // Re-check: another process may have refreshed right before we locked
      const current = await storage.getTokenRecord(sid)
      if (current && !needsRefresh(current.accessTokenExpiresAt, options.earlyRefreshSeconds ?? 30)) {
        return current
      }

      const refreshed = await refreshFn(current ?? record)
      await storage.setTokenRecord(sid, refreshed)
      return refreshed
    }
    finally {
      await releaseLock(storage, sid, lockId)
    }
  }

  // 2. Someone else holds the lock — wait and inspect
  const started = Date.now()
  while (Date.now() - started < maxWaitMs) {
    await sleep(retryIntervalMs)
    const lock = await storage.getLock(sid)
    if (!lock) {
      // Lock released, read winner's token record
      const latest = await storage.getTokenRecord(sid)
      if (!latest) throw new RefreshTokenExpiredError('cleared_by_lock_holder')
      if (!needsRefresh(latest.accessTokenExpiresAt, options.earlyRefreshSeconds ?? 30)) {
        return latest
      }
      // Still needs refresh, retry acquisition
      return guardedRefresh(storage, sid, latest, refreshFn, options)
    }
  }

  throw new Error('token_refresh_timeout')
}

async function tryAcquireLock(
  storage: HuiaStorageAdapter,
  sid: string,
  lockId: string,
  ttlMs: number,
): Promise<boolean> {
  const cur = await storage.getLock(sid)
  if (cur && Date.now() - cur.acquiredAt < ttlMs) {
    return false
  }
  await storage.setLock(sid, { lockId, acquiredAt: Date.now() }, Math.ceil(ttlMs / 1000))
  const check = await storage.getLock(sid)
  return check?.lockId === lockId
}

async function releaseLock(storage: HuiaStorageAdapter, sid: string, lockId: string): Promise<void> {
  const cur = await storage.getLock(sid)
  if (cur?.lockId === lockId) {
    await storage.deleteLock(sid).catch(() => {})
  }
}
