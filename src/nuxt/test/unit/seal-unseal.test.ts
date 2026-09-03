import { describe, it, expect } from 'vitest'
import { sealValue, unsealValue } from '../../src/runtime/server/utils/cookie'
import { testConfig } from '../support/config'
import type { CookiePayload } from '../../src/runtime/server/utils/internal-types'

const payload: CookiePayload = {
  sid: 'b3f10c2e-0000-4000-8000-000000000000',
  user: { sub: 'user-1', name: 'Ada Lovelace', roles: ['admin', 'user'] },
  exp: Date.now() + 1_000_000,
}

describe('sealValue / unsealValue', () => {
  it('round-trips a payload', async () => {
    const cfg = testConfig()
    const sealed = await sealValue(cfg, payload)
    expect(typeof sealed).toBe('string')
    expect(await unsealValue<CookiePayload>(cfg, sealed)).toEqual(payload)
  })

  it('returns null for a wrong password', async () => {
    const sealed = await sealValue(testConfig(), payload)
    const other = testConfig({ session: { ...testConfig().session, password: 'a-totally-different-32-char-password!!!' } })
    expect(await unsealValue(other, sealed)).toBeNull()
  })

  it('returns null for corrupt input', async () => {
    const cfg = testConfig()
    expect(await unsealValue(cfg, 'not-a-sealed-value')).toBeNull()
    expect(await unsealValue(cfg, undefined)).toBeNull()
    expect(await unsealValue(cfg, '')).toBeNull()
  })

  it('never seals a token field into the payload', async () => {
    const cfg = testConfig()
    const sealed = await sealValue(cfg, payload)
    // the sealed string is opaque, but the round-tripped object must only carry the whitelist
    const back = await unsealValue<Record<string, unknown>>(cfg, sealed)
    expect(back).not.toHaveProperty('accessToken')
    expect(back).not.toHaveProperty('access_token')
    expect(back).not.toHaveProperty('refreshToken')
  })
})
