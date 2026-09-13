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
