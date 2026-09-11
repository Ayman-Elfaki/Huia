import { useStorage } from 'nitropack/runtime'
import { randomUUID } from 'uncrypto'
import type { ResolvedHeadlessConfig, TokenRecord, LockRecord } from './internal-types'

const store = (cfg: ResolvedHeadlessConfig) => useStorage(cfg.storageBase)

export const tokenKey = (sid: string) => `sess:${sid}`
export const lockKey = (sid: string) => `lock:${sid}`

export function newSessionId(): string {
  return randomUUID()
}

export function tokenTtlSeconds(cfg: ResolvedHeadlessConfig, record: TokenRecord): number {
  const byRefresh = record.refreshTokenExpiresAt
    ? Math.ceil((record.refreshTokenExpiresAt - Date.now()) / 1000)
    : 0
  return Math.max(byRefresh, cfg.session.maxAge)
}

export async function getTokenRecord(cfg: ResolvedHeadlessConfig, sid: string): Promise<TokenRecord | null> {
  const rec = await store(cfg).getItem<TokenRecord>(tokenKey(sid))
  return rec ?? null
}

export async function setTokenRecord(cfg: ResolvedHeadlessConfig, record: TokenRecord): Promise<void> {
  await store(cfg).setItem(tokenKey(record.sid), record, { ttl: tokenTtlSeconds(cfg, record) })
}

export async function deleteTokenRecord(cfg: ResolvedHeadlessConfig, sid: string): Promise<void> {
  await store(cfg).removeItem(tokenKey(sid))
}

export async function getLock(cfg: ResolvedHeadlessConfig, sid: string): Promise<LockRecord | null> {
  const rec = await store(cfg).getItem<LockRecord>(lockKey(sid))
  return rec ?? null
}

export async function setLock(cfg: ResolvedHeadlessConfig, sid: string, lock: LockRecord): Promise<void> {
  await store(cfg).setItem(lockKey(sid), lock, { ttl: Math.ceil(cfg.refresh.lock.ttlMs / 1000) })
}

export async function deleteLock(cfg: ResolvedHeadlessConfig, sid: string): Promise<void> {
  await store(cfg).removeItem(lockKey(sid))
}
