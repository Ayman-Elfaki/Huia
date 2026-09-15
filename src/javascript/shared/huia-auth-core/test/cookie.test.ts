import { describe, it, expect } from 'vitest'
import {
  splitIntoChunks,
  sealValue,
  unsealValue,
  resolveCookieName,
  assembleChunks,
} from '../src/cookie.js'

describe('cookie primitives', () => {
  const password = 'a-secure-32-byte-password-for-testing-12345'

  it('seals and unseals objects accurately', async () => {
    const payload = { sid: 'sess_123', user: { sub: 'usr_abc', email: 'test@huia.dev' }, expiresAt: Date.now() + 100000 }
    const sealed = await sealValue(password, payload)
    expect(typeof sealed).toBe('string')
    expect(sealed.length).toBeGreaterThan(20)

    const unsealed = await unsealValue<typeof payload>(password, sealed)
    expect(unsealed).toEqual(payload)
  })

  it('returns null on invalid password or corrupted sealed string', async () => {
    const payload = { test: true }
    const sealed = await sealValue(password, payload)
    const resultWrongPass = await unsealValue(password + 'wrong', sealed)
    expect(resultWrongPass).toBeNull()

    const resultCorrupt = await unsealValue(password, sealed.slice(0, -10) + 'badpayload')
    expect(resultCorrupt).toBeNull()
  })

  it('splits and assembles chunks correctly', () => {
    const data = 'abcdefghijklmnopqrstuvwxyz0123456789'
    const chunks = splitIntoChunks(data, 10)
    expect(chunks).toEqual(['abcdefghij', 'klmnopqrst', 'uvwxyz0123', '456789'])

    const store: Record<string, string> = {
      'test_sess.0': chunks[0],
      'test_sess.1': chunks[1],
      'test_sess.2': chunks[2],
      'test_sess.3': chunks[3],
    }

    const reassembled = assembleChunks(name => store[name], 'test_sess', 4)
    expect(reassembled).toBe(data)
  })

  it('resolves cookie name according to secure flag', () => {
    expect(resolveCookieName('__Host-huia_sess', true)).toBe('__Host-huia_sess')
    expect(resolveCookieName('__Host-huia_sess', false)).toBe('huia_sess')
    expect(resolveCookieName('__Secure-huia_sess', false)).toBe('huia_sess')
    expect(resolveCookieName('normal_cookie', false)).toBe('normal_cookie')
  })
})
