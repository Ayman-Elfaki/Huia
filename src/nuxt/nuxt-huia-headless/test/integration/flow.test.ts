import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createApp, toWebHandler } from 'h3'
import { createStorage, type Storage } from 'unstorage'

/* ── shared mocked Nitro runtime ────────────────────────────────────────────── */
let storage: Storage = createStorage()
let raw: Record<string, unknown> = {}

vi.mock('nitropack/runtime', () => ({
  useStorage: () => storage,
  useRuntimeConfig: () => ({ huiaHeadless: raw }),
}))

function makeRaw(over: Record<string, unknown> = {}) {
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
    ...over,
  }
}

async function buildHandler() {
  const app = createApp()
  app.use('/auth/register', (await import('../../src/runtime/server/routes/auth/register.post')).default)
  app.use('/auth/login', (await import('../../src/runtime/server/routes/auth/login.post')).default)
  app.use('/auth/logout', (await import('../../src/runtime/server/routes/auth/logout.post')).default)
  app.use('/auth/session', (await import('../../src/runtime/server/routes/auth/session.get')).default)
  return toWebHandler(app)
}

const H = { 'x-forwarded-proto': 'https' }

const cookieOf = (res: Response, name: string) => {
  for (const sc of res.headers.getSetCookie()) {
    const m = sc.match(new RegExp(`^${name}(?:\\.\\d+)?=([^;]*)`))
    if (m) return sc.split(';')[0]! // "name=value"
  }
  return null
}

/* ── fake Huia.Headless backend ───────────────────────────────────────────── */
let fetchMock: ReturnType<typeof vi.fn>

beforeEach(async () => {
  storage = createStorage()
  raw = makeRaw()

  fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const path = new URL(url).pathname
    const method = init?.method ?? 'GET'

    if (path === '/identity/register' && method === 'POST') {
      return new Response(null, { status: 200 })
    }
    if (path === '/identity/login' && method === 'POST') {
      const body = JSON.parse(String(init?.body))
      if (body.password !== 'correct-horse-battery-staple') {
        return Response.json({ title: 'Failed', status: 401 }, { status: 401 })
      }
      return Response.json({ tokenType: 'Bearer', accessToken: 'at-1', expiresIn: 3600, refreshToken: 'rt-1' })
    }
    if (path === '/identity/me' && method === 'GET') {
      const authz = (init?.headers as Record<string, string> | undefined)?.authorization
      if (authz !== 'Bearer at-1' && authz !== 'Bearer at-2') {
        return Response.json({ title: 'Unauthorized', status: 401 }, { status: 401 })
      }
      return Response.json({
        sub: 'user-ada', email: 'ada@example.test', emailConfirmed: true,
        phoneNumber: null, phoneNumberConfirmed: false,
        firstName: 'Ada', lastName: 'Lovelace', roles: ['admin', 'user'],
      })
    }
    if (path === '/identity/refresh' && method === 'POST') {
      const body = JSON.parse(String(init?.body))
      if (body.refreshToken !== 'rt-1') {
        return Response.json({ title: 'invalid refresh token' }, { status: 401 })
      }
      return Response.json({ tokenType: 'Bearer', accessToken: 'at-2', expiresIn: 3600, refreshToken: 'rt-2' })
    }

    throw new Error(`unexpected request: ${method} ${path}`)
  })

  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('register → login → session → logout', () => {
  it('registers, signs in, reads the session with no tokens leaked, then logs out', async () => {
    const handler = await buildHandler()

    const register = await handler(new Request('https://localhost/auth/register', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ email: 'ada@example.test', password: 'correct-horse-battery-staple' }),
    }))
    expect(register.status).toBe(200)

    const login = await handler(new Request('https://localhost/auth/login', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ email: 'ada@example.test', password: 'correct-horse-battery-staple' }),
    }))
    expect(login.status).toBe(200)
    const loginBody = await login.json() as { user: { sub: string, roles: string[] } }
    expect(loginBody.user).toMatchObject({ sub: 'user-ada', roles: ['admin', 'user'] })

    const sessCookie = cookieOf(login, '__Host-huia_headless_sess')
    expect(sessCookie).not.toBeNull()

    // storage carries the tokens, never the cookie
    const keys = await storage.getKeys('sess:')
    expect(keys.length).toBe(1)
    const rec = await storage.getItem(keys[0]!) as Record<string, unknown>
    expect(rec.accessToken).toBe('at-1')
    expect(rec.refreshToken).toBe('rt-1')

    const session = await handler(new Request('https://localhost/auth/session', {
      headers: { ...H, cookie: sessCookie! },
    }))
    const body = await session.json() as Record<string, unknown>
    expect(session.headers.get('cache-control')).toBe('no-store')
    expect(body).toMatchObject({ loggedIn: true, user: { sub: 'user-ada', email: 'ada@example.test' } })
    expect(JSON.stringify(body)).not.toContain('at-1')
    expect(JSON.stringify(body)).not.toContain('rt-1')

    const logout = await handler(new Request('https://localhost/auth/logout', {
      method: 'POST',
      headers: { ...H, cookie: sessCookie! },
    }))
    expect(logout.status).toBe(200)
    expect(await storage.getKeys('sess:')).toHaveLength(0)

    const afterLogout = await handler(new Request('https://localhost/auth/session', {
      headers: { ...H, cookie: sessCookie! },
    }))
    expect(await afterLogout.json()).toEqual({})
  })

  it('rejects a wrong password without creating a session', async () => {
    const handler = await buildHandler()

    const login = await handler(new Request('https://localhost/auth/login', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ email: 'ada@example.test', password: 'wrong' }),
    }))
    expect(login.status).toBe(401)
    expect(await storage.getKeys('sess:')).toHaveLength(0)
  })

  it('transparently refreshes a near-expired access token and rotates the refresh token', async () => {
    const handler = await buildHandler()

    const login = await handler(new Request('https://localhost/auth/login', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ email: 'ada@example.test', password: 'correct-horse-battery-staple' }),
    }))
    const sessCookie = cookieOf(login, '__Host-huia_headless_sess')!

    // force the stored record to look near-expiry
    const keys = await storage.getKeys('sess:')
    const rec = await storage.getItem(keys[0]!) as Record<string, unknown>
    await storage.setItem(keys[0]!, { ...rec, accessTokenExpiresAt: Date.now() + 5_000 })

    const session = await handler(new Request('https://localhost/auth/session', {
      headers: { ...H, cookie: sessCookie },
    }))
    expect((await session.json() as { loggedIn: boolean }).loggedIn).toBe(true)

    const updated = await storage.getItem(keys[0]!) as Record<string, unknown>
    expect(updated.accessToken).toBe('at-2')
    expect(updated.refreshToken).toBe('rt-2')
  })
})
