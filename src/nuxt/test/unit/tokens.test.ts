import { describe, it, expect } from 'vitest'
import {
  pickUserClaims,
  assertHuiaIssuer,
  isJwtExpired,
  sanitizeReturnTo,
} from '../../src/runtime/server/utils/tokens'

describe('pickUserClaims', () => {
  it('keeps only the whitelist (plus sub) and drops everything else', () => {
    const claims = {
      sub: 'u1',
      name: 'Ada',
      email: 'ada@x.test',
      access_token: 'LEAK',
      ssn: '000-00-0000',
    }
    const picked = pickUserClaims(claims, ['sub', 'name', 'email'])
    expect(picked).toEqual({ sub: 'u1', name: 'Ada', email: 'ada@x.test' })
    expect(picked).not.toHaveProperty('access_token')
    expect(picked).not.toHaveProperty('ssn')
  })

  it('always coerces sub to a string', () => {
    expect(pickUserClaims({ sub: 12345 }, []).sub).toBe('12345')
  })

  it('normalizes the OpenIddict "role" claim (singular) into "roles" (array)', () => {
    // One role: OpenIddict/the JWT collapse a single-valued claim to a scalar string.
    expect(pickUserClaims({ sub: 'u1', role: 'huia.administrator' }, ['roles']))
      .toEqual({ sub: 'u1', roles: ['huia.administrator'] })

    // Several roles: the JWT carries an array under the same singular key.
    expect(pickUserClaims({ sub: 'u1', role: ['a', 'b'] }, ['roles']))
      .toEqual({ sub: 'u1', roles: ['a', 'b'] })
  })

  it('omits roles when the id_token carries no role claim', () => {
    expect(pickUserClaims({ sub: 'u1' }, ['roles'])).toEqual({ sub: 'u1' })
  })
})

describe('assertHuiaIssuer', () => {
  it('passes on an exact per-tenant match', () => {
    expect(() => assertHuiaIssuer('https://id.example.test/acme', 'https://id.example.test/acme')).not.toThrow()
  })
  it('throws on any mismatch (tenant, trailing slash, host)', () => {
    expect(() => assertHuiaIssuer('https://id.example.test/other', 'https://id.example.test/acme')).toThrow(/issuer_mismatch/)
    expect(() => assertHuiaIssuer('https://id.example.test/acme/', 'https://id.example.test/acme')).toThrow(/issuer_mismatch/)
    expect(() => assertHuiaIssuer(undefined, 'https://id.example.test/acme')).toThrow(/issuer_mismatch/)
  })
})

describe('isJwtExpired', () => {
  const mk = (exp: number) => `x.${Buffer.from(JSON.stringify({ exp })).toString('base64url')}.y`
  it('is true for a past exp and false for a future one', () => {
    expect(isJwtExpired(mk(Math.floor(Date.now() / 1000) - 3600))).toBe(true)
    expect(isJwtExpired(mk(Math.floor(Date.now() / 1000) + 3600))).toBe(false)
  })
  it('is true for junk', () => {
    expect(isJwtExpired('not-a-jwt')).toBe(true)
    expect(isJwtExpired('')).toBe(true)
  })
})

describe('sanitizeReturnTo', () => {
  it('accepts same-origin paths', () => {
    expect(sanitizeReturnTo('/dashboard')).toBe('/dashboard')
    expect(sanitizeReturnTo('/x?y=1#z')).toBe('/x?y=1#z')
  })
  it('rejects absolute, protocol-relative, backslash and control-char values', () => {
    expect(sanitizeReturnTo('https://evil.test')).toBe('/')
    expect(sanitizeReturnTo('//evil.test')).toBe('/')
    expect(sanitizeReturnTo('/\\evil.test')).toBe('/')
    expect(sanitizeReturnTo('/foo\nbar')).toBe('/')
    expect(sanitizeReturnTo('/foo bar')).toBe('/')
    expect(sanitizeReturnTo('')).toBe('/')
    expect(sanitizeReturnTo(undefined)).toBe('/')
    expect(sanitizeReturnTo('relative')).toBe('/')
  })
})
