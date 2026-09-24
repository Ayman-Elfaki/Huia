import { NextRequest, NextResponse } from 'next/server'
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
  readSessionFromCookies,
  writeSessionToResponse,
  clearSessionFromResponse,
} from './session.js'
import type { HuiaOidcConfig } from '../types.js'

export function createHuiaOidcHandler(configInput: HuiaOidcConfig) {
  const cfg = resolveOidcConfig(configInput)
  const oauthCookieName = `${cfg.session.name}_oauth`

  // Next.js's NextRequest.url reflects how the server was started (next dev/next start default to
  // "localhost"), not the request's actual Host header — see the appUrl doc comment in types.ts. Every
  // absolute URL the handler builds on its own behalf goes through this instead of url.origin directly.
  const origin = (url: URL): string => cfg.appUrl ?? url.origin

  return async function handler(req: NextRequest): Promise<NextResponse> {
    const url = new URL(req.url)
    const pathname = url.pathname

    // Match action from path: /api/auth/:action
    const segments = pathname.split('/').filter(Boolean)
    const action = segments[segments.length - 1]

    try {
      if (action === 'login') {
        return await handleLogin(req, url)
      }
      else if (action === 'callback') {
        return await handleCallback(req, url)
      }
      else if (action === 'logout') {
        return await handleLogout(req, url)
      }
      else if (action === 'session') {
        return await handleSession(req)
      }

      return NextResponse.json({ error: 'not_found' }, { status: 404 })
    }
    catch (err) {
      const message = err instanceof Error ? err.message : 'auth_error'
      console.error('[next-huia-oidc] handler error:', err)
      return NextResponse.redirect(new URL(`${cfg.routes.error}?error=${encodeURIComponent(message)}`, origin(url)))
    }
  }

  async function handleLogin(req: NextRequest, url: URL): Promise<NextResponse> {
    const oidcConfig = await defaultOidcHelper.getConfiguration(cfg)
    const returnTo = sanitizeReturnTo(url.searchParams.get('returnTo'))

    const extraParams: Record<string, string> = {}
    for (const key of cfg.allowedAuthParams) {
      const val = url.searchParams.get(key)
      if (val) extraParams[key] = val
    }

    // Determine redirectUri: if relative, make absolute with the app's own origin
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

    const res = NextResponse.redirect(redirectTo)
    const sealedState = await sealValue(cfg.session.password, stateRecord, 600)
    res.cookies.set(oauthCookieName, sealedState, {
      httpOnly: true,
      secure: cfg.cookie.secure,
      sameSite: 'lax',
      path: '/',
      maxAge: 600,
    })

    return res
  }

  async function handleCallback(req: NextRequest, url: URL): Promise<NextResponse> {
    const sealedState = req.cookies.get(oauthCookieName)?.value
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
    // openid-client derives the redirect_uri it sends to the token endpoint from this URL's own origin
    // (stripping only the query string) — not from a stored value — so it must carry the same origin
    // handleLogin used to build the redirect_uri the authorization code was issued for, or the token
    // exchange fails with "redirect_uri parameter doesn't match". Swap in origin(url) for exactly that
    // reason (see the appUrl doc comment in types.ts); everything else about the request — path, and
    // critically the query string carrying `code`/`state`/`iss` — stays as-is.
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
    const res = NextResponse.redirect(new URL(redirectTarget, origin(url)))
    await writeSessionToResponse(res, cfg, payload)
    res.cookies.delete(oauthCookieName)

    if (cfg.storage.deleteStateRecord) {
      await cfg.storage.deleteStateRecord(stateRecord.state).catch(() => {})
    }

    return res
  }

  async function handleLogout(req: NextRequest, url: URL): Promise<NextResponse> {
    const payload = await readSessionFromCookies(name => req.cookies.get(name)?.value, cfg)
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

    const res = NextResponse.redirect(logoutUrl)
    clearSessionFromResponse(res, cfg)
    res.cookies.delete(oauthCookieName)
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
}
