import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createApp, toWebHandler } from 'h3'
import { createStorage, type Storage } from 'unstorage'

let storage: Storage = createStorage()
let raw: Record<string, unknown> = {}

vi.mock('nitropack/runtime', () => ({
  useStorage: () => storage,
  useRuntimeConfig: () => ({ huiaHeadless: raw }),
}))

function makeRaw() {
  return {
    baseUrl: 'https://api.example.test',
    session: {
      name: '__Host-huia_headless_sess',
      password: 'integration-test-password-32-chars-minimum!!',
      maxAge: 60 * 60 * 24 * 7,
      cookie: {},
      userClaims: ['sub', 'email', 'firstName', 'lastName', 'roles'],
    },
    storage: { base: 'huia-headless-auth' },
    refresh: { enabled: true, earlyRefreshSeconds: 60, lock: { ttlMs: 5000, waitMs: 800, pollMs: 20 } },
    cookie: { chunkSize: 3800, maxChunks: 8 },
    allowInsecureTls: false,
  }
}

async function buildHandler() {
  const app = createApp()
  app.use('/auth/login', (await import('../../src/runtime/server/routes/auth/login.post')).default)
  app.use('/auth/admin', (await import('../../src/runtime/server/routes/auth/admin')).default)
  return toWebHandler(app)
}

describe('headless admin route integration', () => {
  beforeEach(() => {
    storage = createStorage()
    raw = makeRaw()
  })

  it('rejects unauthenticated caller with 401', async () => {
    const handler = await buildHandler()
    const res = await handler(new Request('https://app.test/auth/admin/users', { method: 'GET' }))
    expect(res.status).toBe(401)
  })

  it('proxies authenticated admin requests to upstream with Bearer token', async () => {
    const handler = await buildHandler()

    // Mock upstream login and me responses
    const fetchMock = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.includes('/identity/login')) {
        return new Response(JSON.stringify({
          tokenType: 'Bearer',
          accessToken: 'admin-access-token',
          expiresIn: 3600,
          refreshToken: 'admin-refresh-token',
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
        const authHeader = (init?.headers as Record<string, string>)?.authorization
        expect(authHeader).toBe('Bearer admin-access-token')
        return new Response(JSON.stringify({
          data: [{ id: 'user-1', email: 'user@example.test' }],
          totalCount: 1,
        }), { status: 200 })
      }
      return new Response(null, { status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    // 1. Log in
    const loginRes = await handler(new Request('https://app.test/auth/login', {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-forwarded-proto': 'https' },
      body: JSON.stringify({ email: 'admin@example.test', password: 'P@ssword123!' }),
    }))
    expect(loginRes.status).toBe(200)
    const cookie = loginRes.headers.getSetCookie()[0]!.split(';')[0]!

    // 2. Call admin users list
    const adminRes = await handler(new Request('https://app.test/auth/admin/users?page=1', {
      method: 'GET',
      headers: { cookie, 'x-forwarded-proto': 'https' },
    }))
    expect(adminRes.status).toBe(200)
    const data = await adminRes.json()
    expect(data.data[0].id).toBe('user-1')
  })
})
