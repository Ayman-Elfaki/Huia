import { describe, it, expect } from 'vitest'
import { createHuiaOidcHandler } from '../src/server/handler.js'
import { sealValue, type CookiePayload } from 'huia-auth-core'
import type { HuiaOidcConfig } from '../src/types.js'

describe('@huia/tanstack-start-oidc handler', () => {
  const password = 'a-secure-32-byte-password-for-testing-12345'
  const config: HuiaOidcConfig = {
    issuer: 'https://id.example.com',
    clientId: 'test-client',
    session: {
      password,
      name: 'test_sess',
    },
    cookie: {
      secure: false,
    },
  }

  const handler = createHuiaOidcHandler(config)

  it('returns 404 for unknown route action', async () => {
    const req = new Request('http://localhost:3000/api/auth/unknown-action')
    const res = await handler(req)

    expect(res.status).toBe(404)
    const body = await res.json()
    expect(body).toEqual({ error: 'not_found' })
  })

  it('supports handler({ request }) call signature', async () => {
    const req = new Request('http://localhost:3000/api/auth/unknown-action')
    const res = await handler({ request: req })

    expect(res.status).toBe(404)
  })

  it('returns loggedIn: false for session endpoint when anonymous', async () => {
    const req = new Request('http://localhost:3000/api/auth/session')
    const res = await handler(req)

    expect(res.status).toBe(200)
    const body = await res.json()
    expect(body.loggedIn).toBe(false)
    expect(body.user).toBeNull()
  })

  it('returns loggedIn: true with user data when session cookie is valid', async () => {
    const payload: CookiePayload = {
      sid: 'test-sid',
      user: { sub: 'u123', email: 'alice@example.com', name: 'Alice' },
      expiresAt: Date.now() + 60000,
    }
    const sealed = await sealValue(password, payload)

    const req = new Request('http://localhost:3000/api/auth/session', {
      headers: {
        cookie: `test_sess=${sealed}`,
      },
    })
    const res = await handler(req)

    expect(res.status).toBe(200)
    const body = await res.json()
    expect(body.loggedIn).toBe(true)
    expect(body.user).toEqual(payload.user)
  })

  it('clears session and redirects on logout', async () => {
    const req = new Request('http://localhost:3000/api/auth/logout')
    const res = await handler(req)

    // Redirect status (302)
    expect(res.status).toBe(302)
    const setCookie = res.headers.get('set-cookie')
    expect(setCookie).toBeDefined()
    expect(setCookie).toContain('test_sess=')
    expect(setCookie).toContain('Max-Age=0')
  })
})
