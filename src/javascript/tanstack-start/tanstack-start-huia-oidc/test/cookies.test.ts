import { describe, it, expect } from 'vitest'
import {
  parseCookieHeader,
  serializeSetCookie,
  writeSessionToHeaders,
  readSessionFromCookies,
  clearSessionFromHeaders,
} from '../src/server/cookies.js'
import type { ResolvedHuiaOidcConfig } from '../src/types.js'
import { defaultStorage, type CookiePayload } from 'huia-auth-core'

describe('@huia/tanstack-start-oidc cookies', () => {
  const dummyConfig: ResolvedHuiaOidcConfig = {
    issuer: 'https://id.example.com',
    clientId: 'test-client',
    redirectUri: '/api/auth/callback',
    scopes: ['openid'],
    par: { enabled: true, required: false },
    allowedAuthParams: [],
    session: {
      name: 'test_sess',
      password: 'password-must-be-32-characters-long-minimum',
      maxAge: 3600,
      userClaims: ['sub', 'email'],
      stateless: false,
    },
    cookie: {
      chunkSize: 100, // Small chunk size to force chunking in test
      maxChunks: 5,
      secure: false,
      sameSite: 'lax',
      path: '/',
    },
    routes: {
      login: '/api/auth/login',
      callback: '/api/auth/callback',
      logout: '/api/auth/logout',
      session: '/api/auth/session',
      error: '/auth/error',
    },
    storage: defaultStorage,
    allowInsecureTls: true,
  }

  it('parses cookie headers correctly', () => {
    const header = 'foo=bar; baz=hello%20world; empty='
    const parsed = parseCookieHeader(header)
    expect(parsed.foo).toBe('bar')
    expect(parsed.baz).toBe('hello world')
    expect(parsed.empty).toBe('')
  })

  it('serializes set-cookie attributes', () => {
    const sc = serializeSetCookie('my_cookie', 'my_val', {
      httpOnly: true,
      secure: true,
      sameSite: 'strict',
      maxAge: 3600,
      path: '/',
    })
    expect(sc).toContain('my_cookie=my_val')
    expect(sc).toContain('HttpOnly')
    expect(sc).toContain('Secure')
    expect(sc).toContain('SameSite=Strict')
    expect(sc).toContain('Max-Age=3600')
    expect(sc).toContain('Path=/')
  })

  it('writes and reads back chunked session cookies', async () => {
    const headers = new Headers()
    const payload: CookiePayload = {
      sid: 'sid-1234567890-abcdef',
      user: { sub: 'sub-user-1', email: 'test@example.com' },
      expiresAt: Date.now() + 100000,
    }

    await writeSessionToHeaders(headers, dummyConfig, payload)

    const setCookies = headers.getSetCookie ? headers.getSetCookie() : [headers.get('set-cookie') || '']
    expect(setCookies.length).toBeGreaterThan(1)

    // Assemble cookies into a map
    const cookieMap: Record<string, string> = {}
    for (const sc of setCookies) {
      const first = sc.split(';')[0]
      const [k, v] = first.split('=')
      if (v) cookieMap[k.trim()] = v.trim()
    }

    const readPayload = await readSessionFromCookies(name => cookieMap[name], dummyConfig)
    expect(readPayload).toBeDefined()
    expect(readPayload?.sid).toBe(payload.sid)
    expect(readPayload?.user).toEqual(payload.user)
  })

  it('clears session cookies properly', () => {
    const headers = new Headers()
    clearSessionFromHeaders(headers, dummyConfig)
    const setCookies = headers.getSetCookie ? headers.getSetCookie() : [headers.get('set-cookie') || '']
    expect(setCookies.some(sc => sc.includes('Max-Age=0'))).toBe(true)
  })
})
