import { describe, it, expect } from 'vitest'
import { NextRequest } from 'next/server'
import { withHuiaOidcAuth } from '../src/middleware.js'
import type { HuiaOidcConfig } from '../src/types.js'

describe('withHuiaOidcAuth middleware', () => {
  const baseConfig: HuiaOidcConfig = {
    issuer: 'https://id.example.com',
    clientId: 'test-client',
    session: {
      password: 'a-secure-32-byte-password-for-testing-12345',
      name: 'test_sess',
    },
    cookie: {
      secure: false, // in test/dev, avoid prefix
    },
    routes: {
      login: '/api/auth/login',
    },
  }

  it('allows public routes through without redirect', async () => {
    const middleware = withHuiaOidcAuth({
      config: baseConfig,
      protectedRoutes: ['/dashboard', '/admin'],
    })

    const req = new NextRequest('http://localhost:3000/public')
    const res = await middleware(req)

    // Not redirected, continues pipeline
    expect(res?.status).toBe(200)
    expect(res?.headers.get('location')).toBeNull()
  })

  it('redirects unauthenticated requests to loginUrl with returnTo', async () => {
    const middleware = withHuiaOidcAuth({
      config: baseConfig,
      protectedRoutes: ['/dashboard', /^\/settings(\/.*)?$/],
    })

    const req = new NextRequest('http://localhost:3000/dashboard?tab=profile')
    const res = await middleware(req)

    expect(res?.status).toBe(307)
    const location = res?.headers.get('location')
    expect(location).toBeDefined()
    const redirectUrl = new URL(location!)
    expect(redirectUrl.pathname).toBe('/api/auth/login')
    expect(redirectUrl.searchParams.get('returnTo')).toBe('/dashboard?tab=profile')
  })

  it('allows access to protected route when valid session cookie exists', async () => {
    const middleware = withHuiaOidcAuth({
      config: baseConfig,
      protectedRoutes: ['/dashboard'],
    })

    const req = new NextRequest('http://localhost:3000/dashboard', {
      headers: {
        cookie: 'test_sess=valid-session-value',
      },
    })
    const res = await middleware(req)

    expect(res?.status).toBe(200)
    expect(res?.headers.get('location')).toBeNull()
  })

  it('supports chunked session cookie format (name.0)', async () => {
    const middleware = withHuiaOidcAuth({
      config: baseConfig,
      protectedRoutes: ['/dashboard'],
    })

    const req = new NextRequest('http://localhost:3000/dashboard', {
      headers: {
        cookie: 'test_sess.0=chunk0-value',
      },
    })
    const res = await middleware(req)

    expect(res?.status).toBe(200)
    expect(res?.headers.get('location')).toBeNull()
  })
})
