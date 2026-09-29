import { describe, it, expect, vi, beforeEach } from 'vitest'
import { MemoryStorageAdapter } from '../src/storage.js'
import type { TokenRecord, AuthStateRecord, LockRecord } from '../src/types.js'

describe('MemoryStorageAdapter', () => {
  let storage: MemoryStorageAdapter

  beforeEach(() => {
    storage = new MemoryStorageAdapter()
  })

  const mockToken: TokenRecord = {
    sid: 's1',
    accessToken: 'at_1',
    claims: { sub: 'u1' },
    accessTokenExpiresAt: Date.now() + 60000,
    updatedAt: Date.now(),
  }

  const mockState: AuthStateRecord = {
    state: 'state_1',
    codeVerifier: 'cv_1',
    nonce: 'n_1',
    createdAt: Date.now(),
  }

  const mockLock: LockRecord = {
    lockId: 'lock_1',
    acquiredAt: Date.now(),
  }

  /* ── Token Records ── */

  describe('token records', () => {
    it('stores and retrieves a token record', async () => {
      await storage.setTokenRecord('s1', mockToken)
      const result = await storage.getTokenRecord('s1')
      expect(result).toEqual(mockToken)
    })

    it('returns null for unknown sid', async () => {
      expect(await storage.getTokenRecord('unknown')).toBeNull()
    })

    it('deletes a token record', async () => {
      await storage.setTokenRecord('s1', mockToken)
      await storage.deleteTokenRecord('s1')
      expect(await storage.getTokenRecord('s1')).toBeNull()
    })

    it('stores without TTL (no expiry)', async () => {
      await storage.setTokenRecord('s1', mockToken)
      // Without TTL the item should persist
      expect(await storage.getTokenRecord('s1')).toEqual(mockToken)
    })

    it('respects TTL and expires the record', async () => {
      vi.useFakeTimers()
      try {
        await storage.setTokenRecord('s1', mockToken, 1) // 1 second TTL
        expect(await storage.getTokenRecord('s1')).toEqual(mockToken)

        vi.advanceTimersByTime(1500) // Advance past TTL
        expect(await storage.getTokenRecord('s1')).toBeNull()
      } finally {
        vi.useRealTimers()
      }
    })
  })

  /* ── State Records ── */

  describe('state records', () => {
    it('stores and retrieves a state record', async () => {
      await storage.setStateRecord!('state_1', mockState)
      const result = await storage.getStateRecord!('state_1')
      expect(result).toEqual(mockState)
    })

    it('returns null for unknown state', async () => {
      expect(await storage.getStateRecord!('unknown')).toBeNull()
    })

    it('deletes a state record', async () => {
      await storage.setStateRecord!('state_1', mockState)
      await storage.deleteStateRecord!('state_1')
      expect(await storage.getStateRecord!('state_1')).toBeNull()
    })

    it('defaults to 600s TTL and expires', async () => {
      vi.useFakeTimers()
      try {
        await storage.setStateRecord!('state_1', mockState) // default 600s
        expect(await storage.getStateRecord!('state_1')).toEqual(mockState)

        vi.advanceTimersByTime(601_000) // Just past 600s
        expect(await storage.getStateRecord!('state_1')).toBeNull()
      } finally {
        vi.useRealTimers()
      }
    })
  })

  /* ── Lock Records ── */

  describe('lock records', () => {
    it('stores and retrieves a lock record', async () => {
      await storage.setLock('s1', mockLock)
      const result = await storage.getLock('s1')
      expect(result).toEqual(mockLock)
    })

    it('returns null for unknown lock', async () => {
      expect(await storage.getLock('unknown')).toBeNull()
    })

    it('deletes a lock record', async () => {
      await storage.setLock('s1', mockLock)
      await storage.deleteLock('s1')
      expect(await storage.getLock('s1')).toBeNull()
    })

    it('defaults to 15s TTL and expires', async () => {
      vi.useFakeTimers()
      try {
        await storage.setLock('s1', mockLock) // default 15s
        expect(await storage.getLock('s1')).toEqual(mockLock)

        vi.advanceTimersByTime(16_000) // Just past 15s
        expect(await storage.getLock('s1')).toBeNull()
      } finally {
        vi.useRealTimers()
      }
    })
  })

  /* ── Expired entry cleanup ── */

  describe('expired entry cleanup', () => {
    it('cleans expired entries across different types on access', async () => {
      vi.useFakeTimers()
      try {
        await storage.setTokenRecord('s1', mockToken, 1) // 1s TTL
        await storage.setTokenRecord('s2', { ...mockToken, sid: 's2' }, 5) // 5s TTL
        await storage.setStateRecord!('state_1', mockState, 1) // 1s TTL
        await storage.setLock('l1', mockLock, 1) // 1s TTL

        vi.advanceTimersByTime(2000) // Past 1s entries but before 5s

        // Expired entries should be cleaned up on access
        expect(await storage.getTokenRecord('s1')).toBeNull()
        expect(await storage.getTokenRecord('s2')).not.toBeNull()
        expect(await storage.getStateRecord!('state_1')).toBeNull()
        expect(await storage.getLock('l1')).toBeNull()
      } finally {
        vi.useRealTimers()
      }
    })
  })

  /* ── Overwrite semantics ── */

  describe('overwrite', () => {
    it('overwrites an existing token record', async () => {
      await storage.setTokenRecord('s1', mockToken)
      const updated = { ...mockToken, accessToken: 'at_new' }
      await storage.setTokenRecord('s1', updated)
      const result = await storage.getTokenRecord('s1')
      expect(result?.accessToken).toBe('at_new')
    })

    it('overwrites an existing lock record', async () => {
      await storage.setLock('s1', mockLock)
      const updated = { lockId: 'lock_2', acquiredAt: Date.now() }
      await storage.setLock('s1', updated)
      const result = await storage.getLock('s1')
      expect(result?.lockId).toBe('lock_2')
    })
  })
})
