import { describe, it, expect, vi } from 'vitest'
import { MemoryStorageAdapter } from '../src/storage.js'
import { ensureFreshTokenRecord } from '../src/refresh.js'
import type { TokenRecord } from '../src/types.js'

describe('guarded refresh', () => {
  it('does not refresh if token is fresh', async () => {
    const storage = new MemoryStorageAdapter()
    const refreshFn = vi.fn()
    const record: TokenRecord = {
      sid: 's1',
      accessToken: 'at_1',
      accessTokenExpiresAt: Date.now() + 100000,
      claims: { sub: 'u1' },
      updatedAt: Date.now(),
    }

    const result = await ensureFreshTokenRecord(storage, record, refreshFn)
    expect(result.accessToken).toBe('at_1')
    expect(refreshFn).not.toHaveBeenCalled()
  })

  it('refreshes and updates storage if token is expiring', async () => {
    const storage = new MemoryStorageAdapter()
    const record: TokenRecord = {
      sid: 's1',
      accessToken: 'at_old',
      accessTokenExpiresAt: Date.now() + 5000, // < 30s
      claims: { sub: 'u1' },
      updatedAt: Date.now(),
    }
    await storage.setTokenRecord('s1', record)

    const refreshFn = vi.fn().mockResolvedValue({
      ...record,
      accessToken: 'at_fresh',
      accessTokenExpiresAt: Date.now() + 300000,
    })

    const result = await ensureFreshTokenRecord(storage, record, refreshFn)
    expect(result.accessToken).toBe('at_fresh')
    expect(refreshFn).toHaveBeenCalledTimes(1)

    const inStorage = await storage.getTokenRecord('s1')
    expect(inStorage?.accessToken).toBe('at_fresh')
  })

  it('deduplicates concurrent refresh calls for same sid', async () => {
    const storage = new MemoryStorageAdapter()
    const record: TokenRecord = {
      sid: 's_concurrent',
      accessToken: 'at_old',
      accessTokenExpiresAt: Date.now() + 5000,
      claims: { sub: 'u1' },
      updatedAt: Date.now(),
    }
    await storage.setTokenRecord('s_concurrent', record)

    let callCount = 0
    const refreshFn = vi.fn(async (rec: TokenRecord) => {
      callCount++
      await new Promise(r => setTimeout(r, 50))
      return {
        ...rec,
        accessToken: 'at_fresh',
        accessTokenExpiresAt: Date.now() + 300000,
      }
    })

    const [res1, res2] = await Promise.all([
      ensureFreshTokenRecord(storage, record, refreshFn),
      ensureFreshTokenRecord(storage, record, refreshFn),
    ])

    expect(res1.accessToken).toBe('at_fresh')
    expect(res2.accessToken).toBe('at_fresh')
    expect(callCount).toBe(1)
  })
})
