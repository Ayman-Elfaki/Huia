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
  parseCookieHeader,
  readSessionFromCookies,
  writeSessionToHeaders,
  clearSessionFromHeaders,
} from './cookies.js'
import type { HuiaHeadlessConfig } from '../types.js'

export function createHuiaHeadlessHandler(configInput: HuiaHeadlessConfig) {
  const cfg = resolveHeadlessConfig(configInput)
  const client = new HuiaHeadlessClient({
    baseUrl: cfg.baseUrl,
    allowInsecureTls: cfg.allowInsecureTls,
  })

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

  async function handleRequest(request: Request): Promise<Response> {
    const url = new URL(request.url)
    const pathname = url.pathname
    const segments = pathname.split('/').filter(Boolean)
    const action = segments[segments.length - 1]
    const method = request.method.toUpperCase()

    try {
      if (method === 'POST' && action === 'register') {
        const body = await request.json()
        await client.register(body)
        return Response.json({ ok: true }, { status: 200 })
      }
      if (method === 'POST' && action === 'login') {
        const body = await request.json()
        const tokens = await client.login(body)
        const { payload, user } = await establishSession(tokens)

        const headers = new Headers({ 'content-type': 'application/json' })
        await writeSessionToHeaders(headers, cfg, payload)
        return new Response(JSON.stringify({ ok: true, user }), { status: 200, headers })
      }
      if (method === 'POST' && action === 'logout') {
        const cookies = parseCookieHeader(request.headers.get('cookie'))
        const payload = await readSessionFromCookies(name => cookies[name], cfg)
        if (!cfg.session.stateless && !payload?.stateless && payload?.sid) {
          await cfg.storage.deleteTokenRecord(payload.sid).catch(() => {})
        }

        const headers = new Headers({ 'content-type': 'application/json' })
        clearSessionFromHeaders(headers, cfg)
        return new Response(JSON.stringify({ ok: true }), { status: 200, headers })
      }
      if (method === 'GET' && action === 'session') {
        const cookies = parseCookieHeader(request.headers.get('cookie'))
        const payload = await readSessionFromCookies(name => cookies[name], cfg)
        if (!payload?.user) {
          return Response.json({ user: null, loggedIn: false }, { status: 200 })
        }
        return Response.json({
          user: payload.user,
          loggedIn: true,
          expiresAt: payload.expiresAt,
        }, { status: 200 })
      }
      if (method === 'POST' && action === 'phone-start') {
        const body = await request.json()
        const result = await client.phoneStart(body)
        return Response.json(result, { status: 200 })
      }
      if (method === 'POST' && action === 'phone-resend') {
        const body = await request.json()
        const result = await client.phoneResend(body)
        return Response.json(result, { status: 200 })
      }
      if (method === 'POST' && action === 'phone-verify') {
        const body = await request.json()
        const result = await client.phoneVerify(body)

        if (result.status === 'requires_profile') {
          return Response.json({
            requiresProfile: true,
            flowId: result.flowId ?? body.flowId,
            maskedPhoneNumber: result.maskedPhoneNumber,
          }, { status: 200 })
        }

        if (result.accessToken) {
          const { payload, user } = await establishSession({
            accessToken: result.accessToken,
            refreshToken: result.refreshToken,
            expiresIn: result.expiresIn,
          })
          const headers = new Headers({ 'content-type': 'application/json' })
          await writeSessionToHeaders(headers, cfg, payload)
          return new Response(JSON.stringify({ requiresProfile: false, user }), { status: 200, headers })
        }

        return Response.json(result, { status: 200 })
      }
      if (method === 'POST' && action === 'phone-complete-profile') {
        const body = await request.json()
        const tokens = await client.phoneCompleteProfile(body)
        const { payload, user } = await establishSession(tokens)

        const headers = new Headers({ 'content-type': 'application/json' })
        await writeSessionToHeaders(headers, cfg, payload)
        return new Response(JSON.stringify({ user }), { status: 200, headers })
      }
      if (method === 'GET' && segments.includes('external') && !segments.includes('exchange') && !segments.includes('complete-profile')) {
        const provider = action
        const returnTo = url.searchParams.get('returnUrl') ?? '/'
        const callbackUrl = `${cfg.appUrl ?? url.origin}/auth/callback?returnTo=${encodeURIComponent(returnTo)}`
        const redirectUrl = client.externalLoginUrl(provider, callbackUrl)
        return new Response(null, {
          status: 302,
          headers: { Location: redirectUrl },
        })
      }
      if (method === 'POST' && action === 'external-exchange') {
        const body = await request.json()
        const result = await client.externalExchange(body.code)

        if (result.requiresProfile) {
          return Response.json({
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
          const headers = new Headers({ 'content-type': 'application/json' })
          await writeSessionToHeaders(headers, cfg, payload)
          return new Response(JSON.stringify({ requiresProfile: false, user }), { status: 200, headers })
        }

        return Response.json(result, { status: 200 })
      }
      if (method === 'POST' && action === 'external-complete-profile') {
        const body = await request.json()
        const tokens = await client.externalCompleteProfile(body)
        const { payload, user } = await establishSession(tokens)

        const headers = new Headers({ 'content-type': 'application/json' })
        await writeSessionToHeaders(headers, cfg, payload)
        return new Response(JSON.stringify({ user }), { status: 200, headers })
      }
      if (segments.includes('admin')) {
        const adminIndex = segments.indexOf('admin')
        const adminSubpath = segments.slice(adminIndex + 1).join('/')
        return await handleAdmin(request, adminSubpath, url.search)
      }

      return Response.json({ error: 'not_found' }, { status: 404 })
    }
    catch (err) {
      if (err instanceof BackendError) {
        return Response.json(err.problem ?? { error: err.message }, { status: err.status })
      }
      console.error('[@huia/tanstack-start-headless] error:', err)
      const message = err instanceof Error ? err.message : 'server_error'
      return Response.json({ error: message }, { status: 500 })
    }
  }

  async function handleAdmin(request: Request, subpath: string, search: string): Promise<Response> {
    const cookies = parseCookieHeader(request.headers.get('cookie'))
    const session = await readSessionFromCookies(name => cookies[name], cfg)
    if (!session) {
      return Response.json({ error: 'unauthorized' }, { status: 401 })
    }

    const record = await cfg.storage.getTokenRecord(session.sid)
    if (!record?.accessToken) {
      return Response.json({ error: 'unauthorized' }, { status: 401 })
    }

    const targetUrl = `${client.baseUrl}/admin/${subpath}${search}`
    const method = request.method.toUpperCase()
    const hasBody = ['POST', 'PUT', 'PATCH'].includes(method)
    const body = hasBody ? await request.text() : undefined

    const headers: Record<string, string> = {
      authorization: `Bearer ${record.accessToken}`,
    }
    const contentType = request.headers.get('content-type')
    if (contentType) {
      headers['content-type'] = contentType
    }

    const res = await fetch(targetUrl, {
      method,
      headers,
      body,
    })

    if (res.status === 204) {
      return new Response(null, { status: 204 })
    }

    const text = await res.text()
    return new Response(text, {
      status: res.status,
      headers: {
        'content-type': res.headers.get('content-type') || 'application/json',
      },
    })
  }

  return function handler(reqOrContext: Request | { request: Request }): Promise<Response> {
    const req = reqOrContext instanceof Request ? reqOrContext : reqOrContext.request
    return handleRequest(req)
  }
}
