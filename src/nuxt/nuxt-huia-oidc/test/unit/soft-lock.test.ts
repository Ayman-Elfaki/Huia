import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createStorage } from 'unstorage'
import { testConfig } from '../support/config'
import type { TokenRecord } from '../../src/runtime/server/utils/internal-types'

const storage = createStorage()

vi.mock('nitropack/runtime', () => ({
  useStorage: () => storage,
  useRuntimeConfig: () => ({ huiaAuth: {} }),
}))

vi.mock('../../src/runtime/server/utils/config', () => ({
  resolveAuthConfig: () => testConfig({ refresh: { enabled: true, earlyRefreshSeconds: 60, lock: { ttlMs: 500, waitMs: 400, pollMs: 10 } } }),
}))

const refreshTokenGrant = vi.fn()
class FakeResponseBodyError extends Error {
  error: string
  constructor(error: string) {
    super(error)
    this.error = error
    this.name = 'ResponseBodyError'
  }
}

vi.mock('openid-client', () => ({
  refreshTokenGrant: (...args: unknown[]) => refreshTokenGrant(...args),
  ResponseBodyError: FakeResponseBodyError,
}))

vi.mock('../../src/runtime/server/utils/oidc', () => ({
  getOidcConfig: vi.fn().mockResolvedValue({}),
  isInvalidGrant: (err: unknown) => err instanceof FakeResponseBodyError && err.error === 'invalid_grant',
}))

// import AFTER the mocks are registered
const { ensureFreshTokens, RefreshTokenExpiredError } = await import('../../src/runtime/server/utils/refresh')

const near = (): TokenRecord => ({
  sid: 'sid-1',
  accessToken: 'old-at',
  refreshToken: 'rt-1',
  idToken: 'old-it',
  tokenType: 'bearer',
  scope: 'openid offline_access',
  claims: { sub: 'u1' },
  accessTokenExpiresAt: Date.now() + 5_000, // within earlyRefreshSeconds
  createdAt: Date.now() - 1_000_000,
  updatedAt: Date.now() - 1_000_000,
})

beforeEach(async () => {
  await storage.clear()
  refreshTokenGrant.mockReset()
})

describe('ensureFreshTokens', () => {
  it('returns the record untouched when it is not near expiry', async () => {
    const rec = { ...near(), accessTokenExpiresAt: Date.now() + 3_600_000 }
    expect(await ensureFreshTokens({} as never, rec)).toBe(rec)
    expect(refreshTokenGrant).not.toHaveBeenCalled()
  })

  it('single-flights concurrent callers — one token request, one shared result', async () => {
    refreshTokenGrant.mockImplementation(async () => {
      await new Promise(r => setTimeout(r, 30))
      return { access_token: 'new-at', refresh_token: 'rt-2', expires_in: 3600, scope: 'openid offline_access', claims: () => undefined }
    })
    const rec = near()
    const [a, b, c] = await Promise.all([
      ensureFreshTokens({} as never, rec),
      ensureFreshTokens({} as never, rec),
      ensureFreshTokens({} as never, rec),
    ])
    expect(refreshTokenGrant).toHaveBeenCalledTimes(1)
    expect(a.accessToken).toBe('new-at')
    expect(a).toEqual(b)
    expect(b).toEqual(c)
    expect(await storage.getItem('sess:sid-1')).toMatchObject({ accessToken: 'new-at', refreshToken: 'rt-2' })
  })

  it('rotates the refresh token and clears the lock afterwards', async () => {
    refreshTokenGrant.mockResolvedValue({
      access_token: 'new-at', refresh_token: 'rt-rotated', id_token: undefined,
      expires_in: 3600, scope: 'openid offline_access', claims: () => undefined,
    })
    const out = await ensureFreshTokens({} as never, near())
    expect(out.refreshToken).toBe('rt-rotated')
    expect(await storage.hasItem('lock:sid-1')).toBe(false)
  })

  it('maps invalid_grant to RefreshTokenExpiredError and deletes the token record', async () => {
    await storage.setItem('sess:sid-1', near())
    refreshTokenGrant.mockRejectedValue(new FakeResponseBodyError('invalid_grant'))
    await expect(ensureFreshTokens({} as never, near())).rejects.toBeInstanceOf(RefreshTokenExpiredError)
    expect(await storage.getItem('sess:sid-1')).toBeNull()
    expect(await storage.hasItem('lock:sid-1')).toBe(false)
  })

  it('propagates a transient error without deleting the record', async () => {
    await storage.setItem('sess:sid-1', near())
    refreshTokenGrant.mockRejectedValue(new Error('ECONNRESET'))
    await expect(ensureFreshTokens({} as never, near())).rejects.toThrow('ECONNRESET')
    expect(await storage.getItem('sess:sid-1')).not.toBeNull()
    expect(await storage.hasItem('lock:sid-1')).toBe(false)
  })
})
