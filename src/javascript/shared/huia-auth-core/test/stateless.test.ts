import { describe, it, expect } from 'vitest'
import {
  sealValue,
  unsealValue,
  splitIntoChunks,
  assembleChunks,
} from '../src/cookie.js'
import type { CookiePayload, TokenRecord } from '../src/types.js'

describe('stateless session mode in huia-auth-core', () => {
  const password = 'a-secure-32-byte-password-for-testing-12345'

  const mockTokenRecord: TokenRecord = {
    sid: 'stateless-session-123',
    accessToken: 'eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c3JfMTIzIn0.signature-placeholder',
    refreshToken: 'def50200-refresh-token-stateless-placeholder',
    idToken: 'eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ1c3JfMTIzIiwiZW1haWwiOiJhbGljZUB0ZXN0LmNvbSJ9.sig',
    claims: {
      sub: 'usr_123',
      email: 'alice@test.com',
      name: 'Alice Tester',
      roles: ['user', 'admin'],
    },
    scope: 'openid profile email offline_access',
    accessTokenExpiresAt: Date.now() + 3600 * 1000,
    refreshTokenExpiresAt: Date.now() + 30 * 86400 * 1000,
    updatedAt: Date.now(),
  }

  it('seals and unseals complete session data in cookie payload without server storage', async () => {
    const payload: CookiePayload = {
      sid: mockTokenRecord.sid,
      user: mockTokenRecord.claims,
      expiresAt: mockTokenRecord.accessTokenExpiresAt,
      tokens: mockTokenRecord,
      stateless: true,
    }

    const sealed = await sealValue(password, payload)
    expect(typeof sealed).toBe('string')

    const unsealed = await unsealValue<CookiePayload>(password, sealed)
    expect(unsealed).not.toBeNull()
    expect(unsealed?.stateless).toBe(true)
    expect(unsealed?.sid).toBe(mockTokenRecord.sid)
    expect(unsealed?.user.email).toBe('alice@test.com')
    expect(unsealed?.tokens).toEqual(mockTokenRecord)
    expect(unsealed?.tokens?.accessToken).toBe(mockTokenRecord.accessToken)
    expect(unsealed?.tokens?.refreshToken).toBe(mockTokenRecord.refreshToken)
  })

  it('handles chunking and reassembly of large stateless payloads', async () => {
    const largePayload: CookiePayload = {
      sid: 'large-stateless-session',
      user: {
        sub: 'usr_large',
        email: 'large@test.com',
        roles: ['admin', 'manager', 'editor', 'auditor', 'operator'],
      },
      expiresAt: Date.now() + 3600 * 1000,
      tokens: {
        ...mockTokenRecord,
        accessToken: 'a'.repeat(2500),
        refreshToken: 'r'.repeat(2500),
      },
      stateless: true,
    }

    const sealed = await sealValue(password, largePayload)
    expect(sealed.length).toBeGreaterThan(3800)

    const chunks = splitIntoChunks(sealed, 3800)
    expect(chunks.length).toBeGreaterThan(1)

    const cookieJar: Record<string, string> = {}
    chunks.forEach((chunk, i) => {
      cookieJar[`huia_sess.${i}`] = chunk
    })

    const assembled = assembleChunks(name => cookieJar[name], 'huia_sess', 8)
    expect(assembled).toBe(sealed)

    const restored = await unsealValue<CookiePayload>(password, assembled!)
    expect(restored?.stateless).toBe(true)
    expect(restored?.tokens?.accessToken).toBe(largePayload.tokens?.accessToken)
  })
})
