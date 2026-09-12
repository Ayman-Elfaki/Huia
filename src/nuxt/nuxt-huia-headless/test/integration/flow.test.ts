import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createApp, createRouter, toWebHandler } from 'h3'
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
    externalCallbackPath: '/auth/callback',
    ...over,
  }
}

async function buildHandler() {
  const app = createApp()
  app.use('/auth/register', (await import('../../src/runtime/server/routes/auth/register.post')).default)
  app.use('/auth/login', (await import('../../src/runtime/server/routes/auth/login.post')).default)
  app.use('/auth/logout', (await import('../../src/runtime/server/routes/auth/logout.post')).default)
  app.use('/auth/session', (await import('../../src/runtime/server/routes/auth/session.get')).default)
  app.use('/auth/phone/start', (await import('../../src/runtime/server/routes/auth/phone/start.post')).default)
  app.use('/auth/phone/verify', (await import('../../src/runtime/server/routes/auth/phone/verify.post')).default)
  app.use('/auth/phone/complete-profile', (await import('../../src/runtime/server/routes/auth/phone/complete-profile.post')).default)
  app.use('/auth/external-exchange', (await import('../../src/runtime/server/routes/auth/external/exchange.post')).default)
  app.use('/auth/external-complete-profile', (await import('../../src/runtime/server/routes/auth/external/complete-profile.post')).default)

  const router = createRouter()
  router.get('/auth/external/:provider', (await import('../../src/runtime/server/routes/auth/external/[provider].get')).default)
  app.use(router)

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
      if (authz === 'Bearer at-1' || authz === 'Bearer at-2') {
        return Response.json({
          sub: 'user-ada', email: 'ada@example.test', emailConfirmed: true,
          phoneNumber: null, phoneNumberConfirmed: false,
          firstName: 'Ada', lastName: 'Lovelace', roles: ['admin', 'user'],
        })
      }
      if (authz?.startsWith('Bearer at-phone-')) {
        return Response.json({
          sub: 'user-phone', email: null, emailConfirmed: false,
          phoneNumber: '+15005551234', phoneNumberConfirmed: true,
          firstName: 'Percy', lastName: 'Phone', roles: [],
        })
      }
      if (authz?.startsWith('Bearer at-ext-')) {
        return Response.json({
          sub: 'user-ext', email: 'nina@ext.test', emailConfirmed: true,
          phoneNumber: null, phoneNumberConfirmed: false,
          firstName: 'Nina', lastName: 'New', roles: [],
        })
      }
      return Response.json({ title: 'Unauthorized', status: 401 }, { status: 401 })
    }
    if (path === '/identity/refresh' && method === 'POST') {
      const body = JSON.parse(String(init?.body))
      if (body.refreshToken !== 'rt-1') {
        return Response.json({ title: 'invalid refresh token' }, { status: 401 })
      }
      return Response.json({ tokenType: 'Bearer', accessToken: 'at-2', expiresIn: 3600, refreshToken: 'rt-2' })
    }

    if (path === '/identity/phone/start' && method === 'POST') {
      const body = JSON.parse(String(init?.body))
      if (body.phoneNumber === '+15005551234') {
        return Response.json({ flowId: 'flow-existing' })
      }
      if (body.phoneNumber === '+15005559999') {
        return Response.json({ flowId: 'flow-new' })
      }
      return Response.json({ error: 'invalid' }, { status: 400 })
    }
    if (path === '/identity/phone/verify' && method === 'POST') {
      const body = JSON.parse(String(init?.body))
      if (body.code !== '123456') {
        return Response.json({ error: 'invalid' }, { status: 400 })
      }
      if (body.flowId === 'flow-existing') {
        return Response.json({ tokenType: 'Bearer', accessToken: 'at-phone-1', expiresIn: 3600, refreshToken: 'rt-phone-1' })
      }
      if (body.flowId === 'flow-new') {
        return Response.json({ flowId: 'flow-new', requiresProfile: true })
      }
      return Response.json({ error: 'invalid' }, { status: 400 })
    }
    if (path === '/identity/phone/complete-profile' && method === 'POST') {
      const body = JSON.parse(String(init?.body))
      if (body.flowId !== 'flow-new') {
        return Response.json({ error: 'invalid' }, { status: 400 })
      }
      return Response.json({ tokenType: 'Bearer', accessToken: 'at-phone-2', expiresIn: 3600, refreshToken: 'rt-phone-2' })
    }
    if (path === '/identity/account/external/exchange' && method === 'POST') {
      const body = JSON.parse(String(init?.body))
      if (body.code === 'ext-code-existing') {
        return Response.json({ tokenType: 'Bearer', accessToken: 'at-ext-1', expiresIn: 3600, refreshToken: 'rt-ext-1' })
      }
      if (body.code === 'ext-code-new') {
        return Response.json({ code: 'ext-code-new', requiresProfile: true, email: 'nina@ext.test', firstName: 'Nina', lastName: 'New' })
      }
      return Response.json({ error: 'invalid' }, { status: 400 })
    }
    if (path === '/identity/account/external/complete-profile' && method === 'POST') {
      const body = JSON.parse(String(init?.body))
      if (body.code !== 'ext-code-new') {
        return Response.json({ error: 'invalid' }, { status: 400 })
      }
      return Response.json({ tokenType: 'Bearer', accessToken: 'at-ext-2', expiresIn: 3600, refreshToken: 'rt-ext-2' })
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

describe('phone login: start → verify → (complete-profile) → session', () => {
  it('signs an existing number in directly, no profile step', async () => {
    const handler = await buildHandler()

    const start = await handler(new Request('https://localhost/auth/phone/start', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ phoneNumber: '+15005551234' }),
    }))
    const { flowId } = await start.json() as { flowId: string }
    expect(flowId).toBe('flow-existing')

    const verify = await handler(new Request('https://localhost/auth/phone/verify', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ flowId, code: '123456' }),
    }))
    expect(verify.status).toBe(200)
    const verifyBody = await verify.json() as { user?: { sub: string } }
    expect(verifyBody.user).toMatchObject({ sub: 'user-phone' })
    expect(cookieOf(verify, '__Host-huia_headless_sess')).not.toBeNull()
  })

  it('routes a new number through complete-profile before establishing a session', async () => {
    const handler = await buildHandler()

    const start = await handler(new Request('https://localhost/auth/phone/start', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ phoneNumber: '+15005559999' }),
    }))
    const { flowId } = await start.json() as { flowId: string }

    const verify = await handler(new Request('https://localhost/auth/phone/verify', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ flowId, code: '123456' }),
    }))
    const verifyBody = await verify.json() as { requiresProfile?: true, flowId?: string }
    expect(verifyBody).toEqual({ requiresProfile: true, flowId: 'flow-new' })
    expect(cookieOf(verify, '__Host-huia_headless_sess')).toBeNull()

    const complete = await handler(new Request('https://localhost/auth/phone/complete-profile', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ flowId: 'flow-new', firstName: 'Percy', lastName: 'Phone' }),
    }))
    expect(complete.status).toBe(200)
    expect(cookieOf(complete, '__Host-huia_headless_sess')).not.toBeNull()
  })
})

describe('external login: challenge redirect → exchange → (complete-profile) → session', () => {
  it('builds the challenge redirect with an absolute, encoded callback returnUrl', async () => {
    const handler = await buildHandler()

    const response = await handler(new Request(
      'https://shop.example.test/auth/external/google?returnUrl=' + encodeURIComponent('/dashboard'),
      // The Fetch API's Request constructor never adds a Host header from the URL itself (a real
      // browser request always carries one) — getRequestURL() needs it to build an absolute URL.
      { headers: { ...H, host: 'shop.example.test' }, redirect: 'manual' },
    ))

    expect(response.status).toBe(302)
    const location = response.headers.get('location')!
    // Huia.Headless's own challenge endpoint, with our callback page (this app's origin) as returnUrl.
    expect(location).toContain('/identity/account/external/google?returnUrl=')
    const callbackUrl = new URL(decodeURIComponent(location.split('returnUrl=')[1]!))
    expect(callbackUrl.origin).toBe('https://shop.example.test')
    expect(callbackUrl.pathname).toBe('/auth/callback')
    expect(callbackUrl.searchParams.get('returnTo')).toBe('/dashboard')
  })

  it('rejects a cross-origin returnUrl on the challenge redirect (open-redirect guard)', async () => {
    const handler = await buildHandler()

    const response = await handler(new Request(
      'https://shop.example.test/auth/external/google?returnUrl=' + encodeURIComponent('https://evil.example.com/'),
      { headers: { ...H, host: 'shop.example.test' }, redirect: 'manual' },
    ))

    // sanitizeReturnTo() rejects the absolute URL and falls back to '/' — never forwards attacker input.
    const location = response.headers.get('location')!
    const callbackUrl = new URL(decodeURIComponent(location.split('returnUrl=')[1]!))
    expect(callbackUrl.searchParams.get('returnTo')).toBe('/')
  })

  it('signs a returning linked account in directly, no profile step', async () => {
    const handler = await buildHandler()

    const exchange = await handler(new Request('https://localhost/auth/external-exchange', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ code: 'ext-code-existing' }),
    }))
    expect(exchange.status).toBe(200)
    const body = await exchange.json() as { user?: { sub: string } }
    expect(body.user).toMatchObject({ sub: 'user-ext' })
    expect(cookieOf(exchange, '__Host-huia_headless_sess')).not.toBeNull()
  })

  it('routes a new sign-up through complete-profile before establishing a session', async () => {
    const handler = await buildHandler()

    const exchange = await handler(new Request('https://localhost/auth/external-exchange', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ code: 'ext-code-new' }),
    }))
    const exchangeBody = await exchange.json() as { requiresProfile?: true, code?: string, firstName?: string }
    expect(exchangeBody).toMatchObject({ requiresProfile: true, code: 'ext-code-new', firstName: 'Nina' })
    expect(cookieOf(exchange, '__Host-huia_headless_sess')).toBeNull()

    const complete = await handler(new Request('https://localhost/auth/external-complete-profile', {
      method: 'POST',
      headers: { ...H, 'content-type': 'application/json' },
      body: JSON.stringify({ code: 'ext-code-new', firstName: 'Nina', lastName: 'New' }),
    }))
    expect(complete.status).toBe(200)
    expect(cookieOf(complete, '__Host-huia_headless_sess')).not.toBeNull()
  })
})
