import { describe, it, expect } from 'vitest'
import {
  pickUserClaims,
  assertHuiaIssuer,
  needsRefresh,
  isJwtExpired,
  sanitizeReturnTo,
} from '../src/tokens.js'

describe('token utilities', () => {
  it('picks only allowed claims and normalizes roles', () => {
    const raw = {
      sub: 'user_1',
      name: 'Alice',
      email: 'alice@example.com',
      role: 'admin',
      internal_secret: 'ignore_me',
    }

    const picked = pickUserClaims(raw, ['name', 'email', 'roles'])
    expect(picked).toEqual({
      sub: 'user_1',
      name: 'Alice',
      email: 'alice@example.com',
      roles: ['admin'],
    })
    expect(picked).not.toHaveProperty('internal_secret')
  })

  it('handles array of roles', () => {
    const raw = {
      sub: 'user_2',
      role: ['admin', 'manager'],
    }
    const picked = pickUserClaims(raw)
    expect(picked.roles).toEqual(['admin', 'manager'])
  })

  it('assertHuiaIssuer throws on mismatch', () => {
    expect(() => assertHuiaIssuer('http://wrong', 'http://expected')).toThrow('issuer_mismatch')
    expect(() => assertHuiaIssuer('http://expected', 'http://expected')).not.toThrow()
  })

  it('checks needsRefresh based on buffer', () => {
    const farFuture = Date.now() + 60000
    const nearFuture = Date.now() + 10000 // 10s < 30s buffer
    expect(needsRefresh(farFuture, 30)).toBe(false)
    expect(needsRefresh(nearFuture, 30)).toBe(true)
  })

  it('sanitizes returnTo urls', () => {
    expect(sanitizeReturnTo('/dashboard')).toBe('/dashboard')
    expect(sanitizeReturnTo('/cart?item=1')).toBe('/cart?item=1')
    expect(sanitizeReturnTo('https://evil.com')).toBe('/')
    expect(sanitizeReturnTo('//evil.com')).toBe('/')
    expect(sanitizeReturnTo('/\\evil.com')).toBe('/')
    expect(sanitizeReturnTo(null)).toBe('/')
  })
})
