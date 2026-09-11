import { describe, it, expect, vi, beforeAll, afterAll } from 'vitest'
import { createApp, toWebHandler } from 'h3'
import { createStorage, type Storage } from 'unstorage'
import { startMockOp, type MockOp } from '../support/mock-op'

/* ── shared mocked Nitro runtime ────────────────────────────────────────────── */
let storage: Storage = createStorage()
let raw: Record<string, unknown> = {}

vi.mock('nitropack/runtime', () => ({
  useStorage: () => storage,
  useRuntimeConfig: () => ({ huiaAuth: raw }),
}))

function makeRaw(op: MockOp, over: Record<string, unknown> = {}) {
  return {
    clientId: 'acme-web',
    clientSecret: 'shhh',
    issuer: '',
    huia: { baseUrl: op.origin, tenant: 'acme' },
    redirectUrl: '/auth/oidc/callback',
    scopes: ['openid', 'profile', 'email', 'offline_access'],
    allowedAuthParams: ['ui_locales'],
    par: { enabled: true, required: false },
    session: {
      name: '__Host-huia_sess',
      password: 'integration-test-password-32-chars-minimum!!',
      maxAge: 60 * 60 * 24 * 7,
      cookie: {},
      userClaims: ['sub', 'name', 'email', 'roles'],
    },
    storage: { base: 'huia-auth' },
    refresh: { enabled: true, earlyRefreshSeconds: 60, lock: { ttlMs: 5000, waitMs: 800, pollMs: 20 } },
    cookie: { chunkSize: 3800, maxChunks: 8 },
    routes: { login: '/auth/oidc/login', callback: '/auth/oidc/callback', logout: '/auth/oidc/logout', session: '/api/_auth/session', error: '/' },
    allowInsecureTls: true,
    logout: { rpInitiated: true },
    ...over,
  }
}

async function buildHandler() {
  const app = createApp()
  app.use('/auth/oidc/login', (await import('../../src/runtime/server/routes/auth/oidc/login.get')).default)
  app.use('/auth/oidc/callback', (await import('../../src/runtime/server/routes/auth/oidc/callback.get')).default)
  app.use('/api/_auth/session', (await import('../../src/runtime/server/api/_auth/session.get')).default)
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
const allSessionCookies = (res: Response) =>
  res.headers.getSetCookie().filter(c => c.startsWith('__Host-huia_sess')).map(c => c.split(';')[0]!)

describe('OIDC flow (PAR)', () => {
  let op: MockOp
  let handler: Awaited<ReturnType<typeof buildHandler>>

  beforeAll(async () => {
    op = await startMockOp({ advertisePar: true })
    storage = createStorage()
    raw = makeRaw(op)
    handler = await buildHandler()
  })
  afterAll(() => op.close())

  it('login pushes a back-channel PAR request and redirects with request_uri (no code_challenge in the browser URL)', async () => {
    const res = await handler(new Request('https://localhost/auth/oidc/login?returnTo=/dash', { headers: H, redirect: 'manual' }))
    expect(res.status).toBe(302)
    const loc = new URL(res.headers.get('location')!)
    expect(loc.pathname).toBe('/acme/connect/authorize')
    expect(loc.searchParams.get('request_uri')).toMatch(/^urn:ietf:params:oauth:request_uri:/)
    expect(loc.searchParams.get('code_challenge')).toBeNull()
    expect(op.parCalls.at(-1)).toMatchObject({ client_id: 'acme-web', code_challenge_method: 'S256' })
    expect(op.parCalls.at(-1)!.code_challenge).toBeTruthy()
    expect(cookieOf(res, '__Host-huia_oauth')).toBeTruthy()
  })

  it('completes the code exchange, stores tokens server-side and exposes a token-free session', async () => {
    // 1. login
    const login = await handler(new Request('https://localhost/auth/oidc/login?returnTo=/dash', { headers: H, redirect: 'manual' }))
    const oauthCookie = cookieOf(login, '__Host-huia_oauth')!
    // 2. follow to the mock OP authorize endpoint
    const authorize = await fetch(login.headers.get('location')!, { redirect: 'manual' })
    const cbUrl = authorize.headers.get('location')!
    expect(cbUrl).toContain('https://localhost/auth/oidc/callback')
    // 3. hit our callback with the oauth-state cookie
    const cb = await handler(new Request(cbUrl, { headers: { ...H, cookie: oauthCookie }, redirect: 'manual' }))
    expect(cb.status).toBe(302)
    expect(cb.headers.get('location')).toBe('/dash')
    const sessCookie = allSessionCookies(cb)[0]!
    expect(sessCookie).toBeTruthy()

    // token endpoint was called form-encoded with a PKCE verifier
    expect(op.tokenCalls.at(-1)).toMatchObject({ grant_type: 'authorization_code' })
    expect(op.tokenCalls.at(-1)!.code_verifier).toBeTruthy()

    // a TokenRecord exists in storage and carries the real tokens
    const keys = await storage.getKeys('sess:')
    expect(keys.length).toBe(1)
    const rec = await storage.getItem(keys[0]!) as Record<string, unknown>
    expect(rec.accessToken).toMatch(/^at-/)
    expect(rec.refreshToken).toMatch(/^rt-/)

    // 4. the session endpoint returns claims but NO tokens
    const session = await handler(new Request('https://localhost/api/_auth/session', { headers: { ...H, cookie: sessCookie } }))
    const body = await session.json() as Record<string, unknown>
    expect(session.headers.get('cache-control')).toBe('no-store')
    expect(body).toMatchObject({ loggedIn: true, user: { sub: 'user-ada', name: 'Ada Lovelace', roles: ['admin', 'user'] } })
    expect(JSON.stringify(body)).not.toContain('at-')
    expect(body).not.toHaveProperty('accessToken')
    expect(body).not.toHaveProperty('access_token')
  })

  it('rejects a callback whose state does not match the oauth cookie', async () => {
    const login = await handler(new Request('https://localhost/auth/oidc/login', { headers: H, redirect: 'manual' }))
    const authorize = await fetch(login.headers.get('location')!, { redirect: 'manual' })
    const cbUrl = authorize.headers.get('location')!
    // no cookie forwarded ⇒ cookieState is undefined ⇒ mismatch
    const cb = await handler(new Request(cbUrl, { headers: H, redirect: 'manual' }))
    expect(cb.status).toBe(302)
    expect(cb.headers.get('location')).toBe('/?auth_error=state_mismatch')
  })
})

describe('OIDC flow (PAR not advertised → front-channel fallback)', () => {
  let op: MockOp
  let handler: Awaited<ReturnType<typeof buildHandler>>

  beforeAll(async () => {
    op = await startMockOp({ advertisePar: false })
    storage = createStorage()
    raw = makeRaw(op)
    handler = await buildHandler()
  })
  afterAll(() => op.close())

  it('falls back to a signed front-channel authorize URL', async () => {
    const res = await handler(new Request('https://localhost/auth/oidc/login', { headers: H, redirect: 'manual' }))
    expect(res.status).toBe(302)
    const loc = new URL(res.headers.get('location')!)
    expect(loc.pathname).toBe('/acme/connect/authorize')
    expect(loc.searchParams.get('request_uri')).toBeNull()
    expect(loc.searchParams.get('code_challenge')).toBeTruthy()
    expect(loc.searchParams.get('code_challenge_method')).toBe('S256')
    expect(op.parCalls.length).toBe(0)
  })

  it('with par.required and no PAR endpoint, redirects to auth_error=par_required', async () => {
    raw = makeRaw(op, { par: { enabled: true, required: true } })
    const res = await handler(new Request('https://localhost/auth/oidc/login', { headers: H, redirect: 'manual' }))
    expect(res.headers.get('location')).toBe('/?auth_error=par_required')
    raw = makeRaw(op)
  })
})
