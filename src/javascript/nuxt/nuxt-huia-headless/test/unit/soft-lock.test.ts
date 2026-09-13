import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createStorage } from 'unstorage'
import { testConfig } from '../support/config'
import type { TokenRecord } from '../../src/runtime/server/utils/internal-types'

const storage = createStorage()

vi.mock('nitropack/runtime', () => ({
  useStorage: () => storage,
  useRuntimeConfig: () => ({ huiaHeadless: {} }),
}))

vi.mock('../../src/runtime/server/utils/config', () => ({
  resolveAuthConfig: () => testConfig({ refresh: { enabled: true, earlyRefreshSeconds: 60, lock: { ttlMs: 500, waitMs: 400, pollMs: 10 } } }),
}))

const refreshAsync = vi.fn()
const meAsync = vi.fn()

class FakeBackendError extends Error {
  constructor(public status: number, public problem: unknown) {
    super(`huia_headless_error: ${status}`)
    this.name = 'BackendError'
  }
}

vi.mock('../../src/runtime/server/utils/backend', () => ({
  refreshAsync: (...args: unknown[]) => refreshAsync(...args),
  meAsync: (...args: unknown[]) => meAsync(...args),
  BackendError: FakeBackendError,
}))

// import AFTER the mocks are registered
const { ensureFreshTokens, RefreshTokenExpiredError } = await import('../../src/runtime/server/utils/refresh')

const near = (): TokenRecord => ({
  sid: 'sid-1',
  accessToken: 'old-at',
  refreshToken: 'rt-1',
  tokenType: 'Bearer',
  claims: { sub: 'u1' },
  accessTokenExpiresAt: Date.now() + 5_000, // within earlyRefreshSeconds
  createdAt: Date.now() - 1_000_000,
  updatedAt: Date.now() - 1_000_000,
})

beforeEach(async () => {
  await storage.clear()
  refreshAsync.mockReset()
  meAsync.mockReset()
  meAsync.mockResolvedValue({
    sub: 'u1', email: 'u1@example.test', emailConfirmed: true,
    phoneNumber: null, phoneNumberConfirmed: false, firstName: 'A', lastName: 'B', roles: [],
  })
})

describe('ensureFreshTokens', () => {
  it('returns the record untouched when it is not near expiry', async () => {
    const rec = { ...near(), accessTokenExpiresAt: Date.now() + 3_600_000 }
    expect(await ensureFreshTokens({} as never, rec)).toBe(rec)
    expect(refreshAsync).not.toHaveBeenCalled()
  })

  it('single-flights concurrent callers — one refresh call, one shared result', async () => {
    refreshAsync.mockImplementation(async () => {
      await new Promise(r => setTimeout(r, 30))
      return { tokenType: 'Bearer', accessToken: 'new-at', refreshToken: 'rt-2', expiresIn: 3600 }
    })
    const rec = near()
    const [a, b, c] = await Promise.all([
      ensureFreshTokens({} as never, rec),
      ensureFreshTokens({} as never, rec),
      ensureFreshTokens({} as never, rec),
    ])
    expect(refreshAsync).toHaveBeenCalledTimes(1)
    expect(a.accessToken).toBe('new-at')
    expect(a).toEqual(b)
    expect(b).toEqual(c)
    expect(await storage.getItem('sess:sid-1')).toMatchObject({ accessToken: 'new-at', refreshToken: 'rt-2' })
  })

  it('rotates the refresh token and clears the lock afterwards', async () => {
    refreshAsync.mockResolvedValue({ tokenType: 'Bearer', accessToken: 'new-at', refreshToken: 'rt-rotated', expiresIn: 3600 })
    const out = await ensureFreshTokens({} as never, near())
    expect(out.refreshToken).toBe('rt-rotated')
    expect(await storage.hasItem('lock:sid-1')).toBe(false)
  })

  it('maps a 401 to RefreshTokenExpiredError and deletes the token record', async () => {
    await storage.setItem('sess:sid-1', near())
    refreshAsync.mockRejectedValue(new FakeBackendError(401, { title: 'invalid refresh token' }))
    await expect(ensureFreshTokens({} as never, near())).rejects.toBeInstanceOf(RefreshTokenExpiredError)
    expect(await storage.getItem('sess:sid-1')).toBeNull()
    expect(await storage.hasItem('lock:sid-1')).toBe(false)
  })

  it('propagates a transient error without deleting the record', async () => {
    await storage.setItem('sess:sid-1', near())
    refreshAsync.mockRejectedValue(new Error('ECONNRESET'))
    await expect(ensureFreshTokens({} as never, near())).rejects.toThrow('ECONNRESET')
    expect(await storage.getItem('sess:sid-1')).not.toBeNull()
    expect(await storage.hasItem('lock:sid-1')).toBe(false)
  })
})
