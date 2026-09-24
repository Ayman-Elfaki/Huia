import { NextRequest, NextResponse } from 'next/server'
import {
  randomUUID,
  pickUserClaims,
  HuiaHeadlessClient,
  BackendError,
  type TokenRecord,
  type CookiePayload,
} from 'huia-auth-core'
import { resolveHeadlessConfig } from './config.js'
import {
  readSessionFromCookies,
  writeSessionToResponse,
  clearSessionFromResponse,
} from './session.js'
import type { HuiaHeadlessConfig } from '../types.js'

export function createHuiaHeadlessHandler(configInput: HuiaHeadlessConfig) {
  const cfg = resolveHeadlessConfig(configInput)
  const client = new HuiaHeadlessClient({
    baseUrl: cfg.baseUrl,
    allowInsecureTls: cfg.allowInsecureTls,
  })

  return async function handler(req: NextRequest): Promise<NextResponse> {
    const url = new URL(req.url)
    const pathname = url.pathname
    const segments = pathname.split('/').filter(Boolean)
    const action = segments[segments.length - 1]
    const method = req.method.toUpperCase()

    try {
      if (method === 'POST' && action === 'register') {
        return await handleRegister(req)
      }
      if (method === 'POST' && action === 'login') {
        return await handleLogin(req)
      }
      if (method === 'POST' && action === 'logout') {
        return await handleLogout(req)
      }
      if (method === 'GET' && action === 'session') {
        return await handleSession(req)
      }
      if (method === 'POST' && action === 'phone-start') {
        return await handlePhoneStart(req)
      }
      if (method === 'POST' && action === 'phone-verify') {
        return await handlePhoneVerify(req)
      }
      if (method === 'POST' && action === 'phone-complete-profile') {
        return await handlePhoneCompleteProfile(req)
      }
      if (method === 'GET' && segments.includes('external') && !segments.includes('exchange') && !segments.includes('complete-profile')) {
        const provider = action
        return handleExternalLogin(url, provider)
      }
      if (method === 'POST' && action === 'external-exchange') {
        return await handleExternalExchange(req)
      }
      if (method === 'POST' && action === 'external-complete-profile') {
        return await handleExternalCompleteProfile(req)
      }
      if (segments.includes('admin')) {
        const adminIndex = segments.indexOf('admin')
        const adminSubpath = segments.slice(adminIndex + 1).join('/')
        return await handleAdmin(req, adminSubpath, url.search)
      }

      return NextResponse.json({ error: 'not_found' }, { status: 404 })
    }
    catch (err) {
      if (err instanceof BackendError) {
        return NextResponse.json(err.problem ?? { error: err.message }, { status: err.status })
      }
      console.error('[next-huia-headless] error:', err)
      const message = err instanceof Error ? err.message : 'server_error'
      return NextResponse.json({ error: message }, { status: 500 })
    }
  }

  async function establishSession(tokens: { accessToken: string; refreshToken?: string; expiresIn?: number }) {
    const me = await client.me(tokens.accessToken)
    const sid = randomUUID()
    const now = Date.now()
    const expiresIn = tokens.expiresIn || 300
    const user = pickUserClaims(me as unknown as Record<string, unknown>, cfg.session.userClaims)

    const tokenRecord: TokenRecord = {
      sid,
      accessToken: tokens.accessToken,
      refreshToken: tokens.refreshToken,
      claims: user,
      accessTokenExpiresAt: now + expiresIn * 1000,
      updatedAt: now,
    }

    if (!cfg.session.stateless) {
      await cfg.storage.setTokenRecord(sid, tokenRecord, cfg.session.maxAge)
    }

    const payload: CookiePayload = {
      sid,
      user,
      expiresAt: tokenRecord.accessTokenExpiresAt,
      ...(cfg.session.stateless ? { tokens: tokenRecord, stateless: true } : {}),
    }

    return { payload, user }
  }

  async function handleRegister(req: NextRequest): Promise<NextResponse> {
    const body = await req.json()
    await client.register(body)
    return NextResponse.json({ ok: true }, { status: 200 })
  }

  async function handleLogin(req: NextRequest): Promise<NextResponse> {
    const body = await req.json()
    const tokens = await client.login(body)
    const { payload, user } = await establishSession(tokens)

    const res = NextResponse.json({ ok: true, user }, { status: 200 })
    await writeSessionToResponse(res, cfg, payload)
    return res
  }

  async function handleLogout(req: NextRequest): Promise<NextResponse> {
    const payload = await readSessionFromCookies(name => req.cookies.get(name)?.value, cfg)
    if (!cfg.session.stateless && !payload?.stateless && payload?.sid) {
      await cfg.storage.deleteTokenRecord(payload.sid).catch(() => {})
    }

    const res = NextResponse.json({ ok: true }, { status: 200 })
    clearSessionFromResponse(res, cfg)
    return res
  }

  async function handleSession(req: NextRequest): Promise<NextResponse> {
    const payload = await readSessionFromCookies(name => req.cookies.get(name)?.value, cfg)
    if (!payload?.user) {
      return NextResponse.json({ user: null, loggedIn: false }, { status: 200 })
    }
    return NextResponse.json({
      user: payload.user,
      loggedIn: true,
      expiresAt: payload.expiresAt,
    }, { status: 200 })
  }

  async function handlePhoneStart(req: NextRequest): Promise<NextResponse> {
    const body = await req.json()
    const result = await client.phoneStart(body)
    return NextResponse.json(result, { status: 200 })
  }

  async function handlePhoneVerify(req: NextRequest): Promise<NextResponse> {
    const body = await req.json()
    const result = await client.phoneVerify(body)

    if (result.requiresProfile) {
      return NextResponse.json({
        requiresProfile: true,
        flowId: result.flowId ?? body.flowId,
        firstName: result.firstName,
        lastName: result.lastName,
      }, { status: 200 })
    }

    if (result.accessToken) {
      const { payload, user } = await establishSession({
        accessToken: result.accessToken,
        refreshToken: result.refreshToken,
        expiresIn: result.expiresIn,
      })
      const res = NextResponse.json({ requiresProfile: false, user }, { status: 200 })
      await writeSessionToResponse(res, cfg, payload)
      return res
    }

    return NextResponse.json(result, { status: 200 })
  }

  async function handlePhoneCompleteProfile(req: NextRequest): Promise<NextResponse> {
    const body = await req.json()
    const tokens = await client.phoneCompleteProfile(body)
    const { payload, user } = await establishSession(tokens)

    const res = NextResponse.json({ user }, { status: 200 })
    await writeSessionToResponse(res, cfg, payload)
    return res
  }

  function handleExternalLogin(url: URL, provider: string): NextResponse {
    const returnTo = url.searchParams.get('returnUrl') ?? '/'
    // cfg.appUrl, not url.origin: Next.js's NextRequest.url reflects how the server was started (next
    // dev/next start default to "localhost"), not the request's actual Host header — see the appUrl doc
    // comment in types.ts.
    const callbackUrl = `${cfg.appUrl ?? url.origin}/auth/callback?returnTo=${encodeURIComponent(returnTo)}`
    const redirectUrl = client.externalLoginUrl(provider, callbackUrl)
    return NextResponse.redirect(redirectUrl)
  }

  async function handleExternalExchange(req: NextRequest): Promise<NextResponse> {
    const body = await req.json()
    const result = await client.externalExchange(body.code)

    if (result.requiresProfile) {
      return NextResponse.json({
        requiresProfile: true,
        code: result.code ?? body.code,
        email: result.email,
        firstName: result.firstName,
        lastName: result.lastName,
      }, { status: 200 })
    }

    if (result.accessToken) {
      const { payload, user } = await establishSession({
        accessToken: result.accessToken,
        refreshToken: result.refreshToken,
        expiresIn: result.expiresIn,
      })
      const res = NextResponse.json({ requiresProfile: false, user }, { status: 200 })
      await writeSessionToResponse(res, cfg, payload)
      return res
    }

    return NextResponse.json(result, { status: 200 })
  }

  async function handleExternalCompleteProfile(req: NextRequest): Promise<NextResponse> {
    const body = await req.json()
    const tokens = await client.externalCompleteProfile(body)
    const { payload, user } = await establishSession(tokens)

    const res = NextResponse.json({ user }, { status: 200 })
    await writeSessionToResponse(res, cfg, payload)
    return res
  }

  async function handleAdmin(req: NextRequest, subpath: string, search: string): Promise<NextResponse> {
    const session = await readSessionFromCookies(name => req.cookies.get(name)?.value, cfg)
    if (!session) {
      return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
    }

    const record = await cfg.storage.getTokenRecord(session.sid)
    if (!record?.accessToken) {
      return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
    }

    const targetUrl = `${client.baseUrl}/admin/${subpath}${search}`
    const method = req.method.toUpperCase()
    const hasBody = ['POST', 'PUT', 'PATCH'].includes(method)
    const body = hasBody ? await req.text() : undefined

    const headers: Record<string, string> = {
      authorization: `Bearer ${record.accessToken}`,
    }
    const contentType = req.headers.get('content-type')
    if (contentType) {
      headers['content-type'] = contentType
    }

    const res = await fetch(targetUrl, {
      method,
      headers,
      body,
    })

    if (res.status === 204) {
      return new NextResponse(null, { status: 204 })
    }

    const text = await res.text()
    return new NextResponse(text, {
      status: res.status,
      headers: {
        'content-type': res.headers.get('content-type') || 'application/json',
      },
    })
  }
}

