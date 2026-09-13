import { describe, it, expect, vi } from 'vitest'
import { NextRequest } from 'next/server'
import { createHuiaHeadlessHandler } from '../src/server/handler.js'

describe('next-huia-headless admin handler', () => {
  it('returns 401 unauthorized when no session cookie is present', async () => {
    const handler = createHuiaHeadlessHandler({
      baseUrl: 'https://api.example.test',
      session: { password: 'a-secure-32-byte-password-for-testing-12345' },
    })

    const req = new NextRequest('https://app.test/api/auth/admin/users', { method: 'GET' })
    const res = await handler(req)
    expect(res.status).toBe(401)
  })

  it('forwards admin requests to upstream with Bearer token when logged in', async () => {
    const fetchMock = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.includes('/identity/login')) {
        return new Response(JSON.stringify({
          tokenType: 'Bearer',
          accessToken: 'next-admin-token',
          expiresIn: 3600,
          refreshToken: 'next-admin-refresh',
        }), { status: 200 })
      }
      if (url.includes('/identity/me')) {
        return new Response(JSON.stringify({
          email: 'admin@example.test',
          isEmailConfirmed: true,
          roles: ['admin'],
          firstName: 'Admin',
          lastName: 'User',
        }), { status: 200 })
      }
      if (url.includes('/admin/users')) {
        const auth = (init?.headers as Record<string, string>)?.authorization
        expect(auth).toBe('Bearer next-admin-token')
        return new Response(JSON.stringify({
          data: [{ id: 'user-next-1', email: 'user@example.test' }],
        }), { status: 200, headers: { 'content-type': 'application/json' } })
      }
      return new Response(null, { status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const handler = createHuiaHeadlessHandler({
      baseUrl: 'https://api.example.test',
      session: { password: 'a-secure-32-byte-password-for-testing-12345' },
    })

    // 1. Log in
    const loginReq = new NextRequest('https://app.test/api/auth/login', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ email: 'admin@example.test', password: 'P@ssword123!' }),
    })
    const loginRes = await handler(loginReq)
    expect(loginRes.status).toBe(200)
    const setCookie = loginRes.headers.get('set-cookie')
    expect(setCookie).toBeTruthy()

    // 2. Access admin
    const adminReq = new NextRequest('https://app.test/api/auth/admin/users?page=1', {
      method: 'GET',
      headers: { cookie: setCookie! },
    })
    const adminRes = await handler(adminReq)
    expect(adminRes.status).toBe(200)
    const data = await adminRes.json()
    expect(data.data[0].id).toBe('user-next-1')
  })
})
