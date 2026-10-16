import {
  randomUUID,
  defaultOidcHelper,
  pickUserClaims,
  sanitizeReturnTo,
  sealValue,
  unsealValue,
  type AuthStateRecord,
  type TokenRecord,
  type CookiePayload,
} from 'huia-auth-core'
import { resolveOidcConfig } from './config.js'
import {
  parseCookieHeader,
  serializeSetCookie,
  readSessionFromCookies,
  writeSessionToHeaders,
  clearSessionFromHeaders,
} from './cookies.js'
import type { HuiaOidcConfig } from '../types.js'

export function createHuiaOidcHandler(configInput: HuiaOidcConfig) {
  const cfg = resolveOidcConfig(configInput)
  const oauthCookieName = `${cfg.session.name}_oauth`

  const origin = (url: URL): string => cfg.appUrl ?? url.origin

  async function handleRequest(request: Request): Promise<Response> {
    const url = new URL(request.url)
    const pathname = url.pathname
    const segments = pathname.split('/').filter(Boolean)
    const action = segments[segments.length - 1]

    try {
      if (action === 'login') {
        return await handleLogin(request, url)
      }
      else if (action === 'callback') {
        return await handleCallback(request, url)
      }
      else if (action === 'logout') {
        return await handleLogout(request, url)
      }
      else if (action === 'session') {
        return await handleSession(request)
      }

      return Response.json({ error: 'not_found' }, { status: 404 })
    }
    catch (err) {
      const message = err instanceof Error ? err.message : 'auth_error'
      console.error('[@huia/tanstack-start-oidc] handler error:', err)
      const errorUrl = new URL(`${cfg.routes.error}?error=${encodeURIComponent(message)}`, origin(url))
      return new Response(null, {
        status: 302,
        headers: {
          Location: errorUrl.toString(),
        },
      })
    }
  }

  async function handleLogin(request: Request, url: URL): Promise<Response> {
    const oidcConfig = await defaultOidcHelper.getConfiguration(cfg)
    const returnTo = sanitizeReturnTo(url.searchParams.get('returnTo'))

    const extraParams: Record<string, string> = {}
    for (const key of cfg.allowedAuthParams) {
      const val = url.searchParams.get(key)
      if (val) extraParams[key] = val
    }

    const redirectUri = cfg.redirectUri.startsWith('http')
      ? cfg.redirectUri
      : `${origin(url)}${cfg.redirectUri}`

    const { redirectTo, stateRecord } = await defaultOidcHelper.beginAuthorization(oidcConfig, {
      clientId: cfg.clientId,
      redirectUri,
      scopes: cfg.scopes,
      returnTo,
      extraParams,
      parEnabled: cfg.par.enabled,
      parRequired: cfg.par.required,
    })

    if (cfg.storage.setStateRecord) {
      await cfg.storage.setStateRecord(stateRecord.state, stateRecord, 600)
    }

    const sealedState = await sealValue(cfg.session.password, stateRecord, 600)
    const headers = new Headers()
    headers.set('Location', redirectTo.toString())
    headers.append('Set-Cookie', serializeSetCookie(oauthCookieName, sealedState, {
      httpOnly: true,
      secure: cfg.cookie.secure,
      sameSite: 'lax',
      path: '/',
      maxAge: 600,
    }))

    return new Response(null, {
      status: 302,
      headers,
    })
  }

  async function handleCallback(request: Request, url: URL): Promise<Response> {
    const cookieHeader = request.headers.get('cookie')
    const cookies = parseCookieHeader(cookieHeader)
    const sealedState = cookies[oauthCookieName]
    let stateRecord: AuthStateRecord | null = null

    if (sealedState) {
      stateRecord = await unsealValue<AuthStateRecord>(cfg.session.password, sealedState, 600)
    }
    if (!stateRecord && cfg.storage.getStateRecord) {
      const stateParam = url.searchParams.get('state')
      if (stateParam) {
        stateRecord = await cfg.storage.getStateRecord(stateParam)
      }
    }

    if (!stateRecord) {
      throw new Error('oauth_state_missing_or_expired')
    }

    const oidcConfig = await defaultOidcHelper.getConfiguration(cfg)
    const callbackUrl = new URL(`${url.pathname}${url.search}`, origin(url))
    const { tokens, claims, returnTo } = await defaultOidcHelper.completeAuthorization(
      oidcConfig,
      callbackUrl,
      stateRecord,
      cfg.issuer,
    )

    const sid = randomUUID()
    const now = Date.now()
    const expiresIn = Number(tokens.expires_in) || 300
    const refreshExpiresIn = typeof tokens.refresh_expires_in === 'number'
      ? tokens.refresh_expires_in
      : undefined

    const user = pickUserClaims(claims, cfg.session.userClaims)

    const tokenRecord: TokenRecord = {
      sid,
      accessToken: tokens.access_token,
      refreshToken: tokens.refresh_token,
      idToken: tokens.id_token,
      claims: user,
      scope: tokens.scope,
      accessTokenExpiresAt: now + expiresIn * 1000,
      refreshTokenExpiresAt: refreshExpiresIn ? now + refreshExpiresIn * 1000 : undefined,
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

    const redirectTarget = returnTo && returnTo.startsWith('/') ? returnTo : '/'
    const redirectUrl = new URL(redirectTarget, origin(url))
    const headers = new Headers()
    headers.set('Location', redirectUrl.toString())

    await writeSessionToHeaders(headers, cfg, payload)
    headers.append('Set-Cookie', serializeSetCookie(oauthCookieName, '', {
      httpOnly: true,
      secure: cfg.cookie.secure,
      sameSite: 'lax',
      path: '/',
      maxAge: 0,
    }))

    if (cfg.storage.deleteStateRecord) {
      await cfg.storage.deleteStateRecord(stateRecord.state).catch(() => {})
    }

    return new Response(null, {
      status: 302,
      headers,
    })
  }

  async function handleLogout(request: Request, url: URL): Promise<Response> {
    const cookieHeader = request.headers.get('cookie')
    const cookies = parseCookieHeader(cookieHeader)
    const payload = await readSessionFromCookies(name => cookies[name], cfg)
    let idTokenHint: string | undefined

    if (payload?.stateless || cfg.session.stateless) {
      idTokenHint = payload?.tokens?.idToken
    }
    else if (payload?.sid) {
      const record = await cfg.storage.getTokenRecord(payload.sid)
      idTokenHint = record?.idToken
      await cfg.storage.deleteTokenRecord(payload.sid).catch(() => {})
      await cfg.storage.deleteLock?.(payload.sid).catch(() => {})
    }

    const postLogoutRedirectUri = `${origin(url)}/`
    let logoutUrl = `${origin(url)}/`

    try {
      const oidcConfig = await defaultOidcHelper.getConfiguration(cfg)
      logoutUrl = defaultOidcHelper.buildLogoutUrl(oidcConfig, cfg.issuer, {
        clientId: cfg.clientId,
        postLogoutRedirectUri,
        idTokenHint,
      })
    }
    catch {
      // Fallback if discovery fails during logout
    }

    const headers = new Headers()
    headers.set('Location', logoutUrl)
    clearSessionFromHeaders(headers, cfg)
    headers.append('Set-Cookie', serializeSetCookie(oauthCookieName, '', {
      httpOnly: true,
      secure: cfg.cookie.secure,
      sameSite: 'lax',
      path: '/',
      maxAge: 0,
    }))

    return new Response(null, {
      status: 302,
      headers,
    })
  }

  async function handleSession(request: Request): Promise<Response> {
    const cookieHeader = request.headers.get('cookie')
    const cookies = parseCookieHeader(cookieHeader)
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

  return function handler(reqOrContext: Request | { request: Request }): Promise<Response> {
    const req = reqOrContext instanceof Request ? reqOrContext : reqOrContext.request
    return handleRequest(req)
  }
}
