import { describe, it, expect } from 'vitest'
import { NextRequest } from 'next/server'
import { createHuiaHeadlessHandler } from '../src/server/handler.js'
import { sealValue, type CookiePayload } from 'huia-auth-core'
import type { HuiaHeadlessConfig } from '../src/types.js'

describe('createHuiaHeadlessHandler', () => {
  const password = 'a-secure-32-byte-password-for-testing-12345'
  const config: HuiaHeadlessConfig = {
    baseUrl: 'https://api.example.com',
    session: {
      password,
      name: 'headless_sess',
    },
    cookie: {
      secure: false,
    },
  }

  const handler = createHuiaHeadlessHandler(config)

  it('returns 404 for unknown route action', async () => {
    const req = new NextRequest('http://localhost:3000/api/auth/unknown-endpoint', {
      method: 'GET',
    })
    const res = await handler(req)

    expect(res.status).toBe(404)
    const body = await res.json()
    expect(body).toEqual({ error: 'not_found' })
  })

  it('returns loggedIn: false for session endpoint when anonymous', async () => {
    const req = new NextRequest('http://localhost:3000/api/auth/session', {
      method: 'GET',
    })
    const res = await handler(req)

    expect(res.status).toBe(200)
    const body = await res.json()
    expect(body.loggedIn).toBe(false)
    expect(body.user).toBeNull()
  })

  it('returns loggedIn: true with user data when session cookie is valid', async () => {
    const payload: CookiePayload = {
      sid: 'headless-sid-1',
      user: { sub: 'u99', email: 'headless@example.com', firstName: 'Head', lastName: 'Less' },
      expiresAt: Date.now() + 60000,
    }
    const sealed = await sealValue(password, payload)

    const req = new NextRequest('http://localhost:3000/api/auth/session', {
      method: 'GET',
      headers: {
        cookie: `headless_sess=${sealed}`,
      },
    })
    const res = await handler(req)

    expect(res.status).toBe(200)
    const body = await res.json()
    expect(body.loggedIn).toBe(true)
    expect(body.user).toEqual(payload.user)
  })

  it('clears session cookie on logout', async () => {
    const req = new NextRequest('http://localhost:3000/api/auth/logout', {
      method: 'POST',
    })
    const res = await handler(req)

    expect(res.status).toBe(200)
    const body = await res.json()
    expect(body.ok).toBe(true)

    const setCookie = res.headers.get('set-cookie')
    expect(setCookie).toBeDefined()
    expect(setCookie).toContain('headless_sess=')
    expect(setCookie).toContain('1970')
  })
})
