import type { Redis } from 'ioredis'
import type {
  HuiaStorageAdapter,
  TokenRecord,
  AuthStateRecord,
  LockRecord,
} from './types.js'

interface StoredItem<T> {
  value: T
  expiresAt: number | null
}

/**
 * In-memory storage adapter with TTL support.
 * Thread-safe for single process Node / Next.js / dev servers.
 */
export class MemoryStorageAdapter implements HuiaStorageAdapter {
  private tokens = new Map<string, StoredItem<TokenRecord>>()
  private states = new Map<string, StoredItem<AuthStateRecord>>()
  private locks = new Map<string, StoredItem<LockRecord>>()

  private cleanExpired<T>(map: Map<string, StoredItem<T>>): void {
    const now = Date.now()
    for (const [key, item] of map.entries()) {
      if (item.expiresAt !== null && item.expiresAt <= now) {
        map.delete(key)
      }
    }
  }

  /* ── Tokens ── */
  async getTokenRecord(sid: string): Promise<TokenRecord | null> {
    this.cleanExpired(this.tokens)
    const item = this.tokens.get(sid)
    if (!item) return null
    if (item.expiresAt !== null && item.expiresAt <= Date.now()) {
      this.tokens.delete(sid)
      return null
    }
    return item.value
  }

  async setTokenRecord(sid: string, record: TokenRecord, ttlSeconds?: number): Promise<void> {
    const expiresAt = ttlSeconds ? Date.now() + ttlSeconds * 1000 : null
    this.tokens.set(sid, { value: record, expiresAt })
  }

  async deleteTokenRecord(sid: string): Promise<void> {
    this.tokens.delete(sid)
  }

  /* ── Auth State ── */
  async getStateRecord(state: string): Promise<AuthStateRecord | null> {
    this.cleanExpired(this.states)
    const item = this.states.get(state)
    if (!item) return null
    if (item.expiresAt !== null && item.expiresAt <= Date.now()) {
      this.states.delete(state)
      return null
    }
    return item.value
  }

  async setStateRecord(state: string, record: AuthStateRecord, ttlSeconds = 600): Promise<void> {
    const expiresAt = Date.now() + ttlSeconds * 1000
    this.states.set(state, { value: record, expiresAt })
  }

  async deleteStateRecord(state: string): Promise<void> {
    this.states.delete(state)
  }

  /* ── Locks ── */
  async getLock(sid: string): Promise<LockRecord | null> {
    this.cleanExpired(this.locks)
    const item = this.locks.get(sid)
    if (!item) return null
    if (item.expiresAt !== null && item.expiresAt <= Date.now()) {
      this.locks.delete(sid)
      return null
    }
    return item.value
  }

  async setLock(sid: string, lock: LockRecord, ttlSeconds = 15): Promise<void> {
    const expiresAt = Date.now() + ttlSeconds * 1000
    this.locks.set(sid, { value: lock, expiresAt })
  }

  async deleteLock(sid: string): Promise<void> {
    this.locks.delete(sid)
  }
}

/** Default singleton in-memory storage */
export const defaultStorage = new MemoryStorageAdapter()

/**
 * Redis-backed storage adapter. Required for the Next.js flavors (next-huia-oidc,
 * next-huia-headless) as soon as more than one server process is in play — which in practice means
 * almost immediately: Next.js's App Router compiles each Route Handler as an independent bundle, and
 * module-level state like {@link MemoryStorageAdapter}'s Map is not reliably shared between them (a
 * token record written by the login route can be invisible to a completely different route reading it
 * back moments later). The Nuxt flavors don't need this — they use Nitro's own `useStorage()` instead,
 * which has no such isolation.
 */
export class RedisStorageAdapter implements HuiaStorageAdapter {
  private readonly redis: Redis
  private readonly keyPrefix: string

  constructor(redis: Redis, keyPrefix = 'huia:') {
    this.redis = redis
    this.keyPrefix = keyPrefix
  }

  private key(kind: 'token' | 'state' | 'lock', id: string): string {
    return `${this.keyPrefix}${kind}:${id}`
  }

  private async getJson<T>(key: string): Promise<T | null> {
    const raw = await this.redis.get(key)
    return raw ? JSON.parse(raw) as T : null
  }

  private async setJson(key: string, value: unknown, ttlSeconds?: number): Promise<void> {
    const payload = JSON.stringify(value)
    if (ttlSeconds) {
      await this.redis.set(key, payload, 'EX', ttlSeconds)
    }
    else {
      await this.redis.set(key, payload)
    }
  }

  /* ── Tokens ── */
  getTokenRecord(sid: string): Promise<TokenRecord | null> {
    return this.getJson<TokenRecord>(this.key('token', sid))
  }

  setTokenRecord(sid: string, record: TokenRecord, ttlSeconds?: number): Promise<void> {
    return this.setJson(this.key('token', sid), record, ttlSeconds)
  }

  async deleteTokenRecord(sid: string): Promise<void> {
    await this.redis.del(this.key('token', sid))
  }

  /* ── Auth State ── */
  getStateRecord(state: string): Promise<AuthStateRecord | null> {
    return this.getJson<AuthStateRecord>(this.key('state', state))
  }

  setStateRecord(state: string, record: AuthStateRecord, ttlSeconds = 600): Promise<void> {
    return this.setJson(this.key('state', state), record, ttlSeconds)
  }

  async deleteStateRecord(state: string): Promise<void> {
    await this.redis.del(this.key('state', state))
  }

  /* ── Locks ── */
  getLock(sid: string): Promise<LockRecord | null> {
    return this.getJson<LockRecord>(this.key('lock', sid))
  }

  setLock(sid: string, lock: LockRecord, ttlSeconds = 15): Promise<void> {
    return this.setJson(this.key('lock', sid), lock, ttlSeconds)
  }

  async deleteLock(sid: string): Promise<void> {
    await this.redis.del(this.key('lock', sid))
  }
}
