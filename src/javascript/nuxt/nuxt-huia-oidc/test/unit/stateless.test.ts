import { describe, it, expect } from 'vitest'
import { sealValue, unsealValue } from '../../src/runtime/server/utils/cookie'
import { testConfig } from '../support/config'
import type { CookiePayload, TokenRecord } from '../../src/runtime/server/utils/internal-types'

describe('nuxt-huia-oidc stateless mode', () => {
  const mockTokenRecord: TokenRecord = {
    sid: 'stateless-session-uuid-1234',
    accessToken: 'sample-access-token-for-stateless-mode',
    refreshToken: 'sample-refresh-token-for-stateless-mode',
    idToken: 'sample-id-token',
    tokenType: 'bearer',
    scope: 'openid profile email',
    claims: { sub: 'user_123', email: 'ada@lovelace.test' },
    accessTokenExpiresAt: Date.now() + 3600 * 1000,
    createdAt: Date.now(),
    updatedAt: Date.now(),
  }

  it('seals and unseals stateless CookiePayload carrying TokenRecord', async () => {
    const cfg = testConfig({
      session: {
        name: '__Host-huia_sess',
        password: 'test-password-at-least-32-characters-long!!',
        maxAge: 60 * 60 * 24 * 7,
        userClaims: ['sub', 'email'],
        stateless: true,
      },
    })

    const payload: CookiePayload = {
      sid: mockTokenRecord.sid,
      user: { sub: 'user_123', email: 'ada@lovelace.test' },
      exp: Date.now() + 1_000_000,
      tokens: mockTokenRecord,
      stateless: true,
    }

    const sealed = await sealValue(cfg, payload)
    expect(typeof sealed).toBe('string')

    const unsealed = await unsealValue<CookiePayload>(cfg, sealed)
    expect(unsealed).not.toBeNull()
    expect(unsealed?.stateless).toBe(true)
    expect(unsealed?.tokens?.accessToken).toBe(mockTokenRecord.accessToken)
    expect(unsealed?.tokens?.refreshToken).toBe(mockTokenRecord.refreshToken)
    expect(unsealed?.user.email).toBe('ada@lovelace.test')
  })
})
