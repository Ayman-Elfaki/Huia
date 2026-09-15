import { useStorage } from 'nitropack/runtime'
import { randomUUID } from 'uncrypto'
import type { ResolvedAuthConfig, TokenRecord, LockRecord } from './internal-types'

const store = (cfg: ResolvedAuthConfig) => useStorage(cfg.storageBase)

export const tokenKey = (sid: string) => `sess:${sid}`
export const lockKey = (sid: string) => `lock:${sid}`

export function newSessionId(): string {
  return randomUUID()
}

export async function getTokenRecord(cfg: ResolvedAuthConfig, sid: string): Promise<TokenRecord | null> {
  const rec = await store(cfg).getItem<TokenRecord>(tokenKey(sid))
  return rec ?? null
}

export async function setTokenRecord(cfg: ResolvedAuthConfig, record: TokenRecord): Promise<void> {
  await store(cfg).setItem(tokenKey(record.sid), record, { ttl: cfg.session.maxAge })
}

export async function deleteTokenRecord(cfg: ResolvedAuthConfig, sid: string): Promise<void> {
  await store(cfg).removeItem(tokenKey(sid))
}

export async function getLock(cfg: ResolvedAuthConfig, sid: string): Promise<LockRecord | null> {
  const rec = await store(cfg).getItem<LockRecord>(lockKey(sid))
  return rec ?? null
}

export async function setLock(cfg: ResolvedAuthConfig, sid: string, lock: LockRecord): Promise<void> {
  await store(cfg).setItem(lockKey(sid), lock, { ttl: Math.ceil(cfg.refresh.lock.ttlMs / 1000) })
}

export async function deleteLock(cfg: ResolvedAuthConfig, sid: string): Promise<void> {
  await store(cfg).removeItem(lockKey(sid))
}

export async function hasLock(cfg: ResolvedAuthConfig, sid: string): Promise<boolean> {
  return store(cfg).hasItem(lockKey(sid))
}
