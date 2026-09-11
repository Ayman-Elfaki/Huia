import { useStorage } from 'nitropack/runtime'
import { randomUUID } from 'uncrypto'
import type { ResolvedAuthConfig, TokenRecord, AuthStateRecord, LockRecord } from './internal-types'

const store = (cfg: ResolvedAuthConfig) => useStorage(cfg.storageBase)

export const tokenKey = (sid: string) => `sess:${sid}`
export const lockKey = (sid: string) => `lock:${sid}`
export const stateKey = (state: string) => `state:${state}`

export function newSessionId(): string {
  return randomUUID()
}

/** Seconds of storage TTL to give a token record (a little past the refresh token, else the session). */
export function tokenTtlSeconds(cfg: ResolvedAuthConfig, record: TokenRecord): number {
  const byRefresh = record.refreshTokenExpiresAt
    ? Math.ceil((record.refreshTokenExpiresAt - Date.now()) / 1000)
    : 0
  return Math.max(byRefresh, cfg.session.maxAge)
}

export async function getTokenRecord(cfg: ResolvedAuthConfig, sid: string): Promise<TokenRecord | null> {
  const rec = await store(cfg).getItem<TokenRecord>(tokenKey(sid))
  return rec ?? null
}

export async function setTokenRecord(cfg: ResolvedAuthConfig, record: TokenRecord): Promise<void> {
  await store(cfg).setItem(tokenKey(record.sid), record, { ttl: tokenTtlSeconds(cfg, record) })
}

export async function deleteTokenRecord(cfg: ResolvedAuthConfig, sid: string): Promise<void> {
  await store(cfg).removeItem(tokenKey(sid))
}

export async function getStateRecord(cfg: ResolvedAuthConfig, state: string): Promise<AuthStateRecord | null> {
  const rec = await store(cfg).getItem<AuthStateRecord>(stateKey(state))
  return rec ?? null
}

export async function setStateRecord(cfg: ResolvedAuthConfig, record: AuthStateRecord, ttlSeconds: number): Promise<void> {
  await store(cfg).setItem(stateKey(record.state), record, { ttl: ttlSeconds })
}

export async function deleteStateRecord(cfg: ResolvedAuthConfig, state: string): Promise<void> {
  await store(cfg).removeItem(stateKey(state))
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
