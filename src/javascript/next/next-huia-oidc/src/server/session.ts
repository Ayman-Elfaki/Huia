import { cookies } from 'next/headers'
import { NextResponse } from 'next/server'
import {
  sealValue,
  unsealValue,
  splitIntoChunks,
  assembleChunks,
  resolveCookieName,
  defaultOidcHelper,
  ensureFreshTokenRecord,
  pickUserClaims,
  type CookiePayload,
  type UserSession,
  type TokenRecord,
} from 'huia-auth-core'
import { resolveOidcConfig } from './config.js'
import type { HuiaOidcConfig, ResolvedHuiaOidcConfig } from '../types.js'

export async function readSessionFromCookies(
  cookieGetter: (name: string) => string | undefined,
  configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig,
): Promise<CookiePayload | null> {
  const cfg = resolveOidcConfig(configInput)
  const name = resolveCookieName(cfg.session.name, cfg.cookie.secure)
  const sealed = assembleChunks(cookieGetter, name, cfg.cookie.maxChunks)
  if (!sealed) return null
  return unsealValue<CookiePayload>(cfg.session.password, sealed, cfg.session.maxAge)
}

export async function writeSessionToResponse(
  response: NextResponse,
  configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig,
  payload: CookiePayload,
): Promise<void> {
  const cfg = resolveOidcConfig(configInput)
  const sealed = await sealValue(cfg.session.password, payload, cfg.session.maxAge)
  const name = resolveCookieName(cfg.session.name, cfg.cookie.secure)
  const limit = cfg.cookie.chunkSize

  const cookieBase = {
    httpOnly: true,
    secure: cfg.cookie.secure,
    sameSite: cfg.cookie.sameSite,
    path: cfg.cookie.path,
    maxAge: cfg.session.maxAge,
  }

  if (sealed.length <= limit) {
    response.cookies.set(name, sealed, cookieBase)
    // Clear any leftover chunk cookies
    for (let i = 0; i < cfg.cookie.maxChunks; i++) {
      response.cookies.delete(`${name}.${i}`)
    }
    return
  }

  const chunks = splitIntoChunks(sealed, limit)
  if (chunks.length > cfg.cookie.maxChunks) {
    throw new Error(`[next-huia-oidc] Session cookie exceeds maximum chunk count (${chunks.length} > ${cfg.cookie.maxChunks}).`)
  }

  response.cookies.delete(name)
  chunks.forEach((chunk, index) => {
    response.cookies.set(`${name}.${index}`, chunk, cookieBase)
  })
  for (let i = chunks.length; i < cfg.cookie.maxChunks; i++) {
    response.cookies.delete(`${name}.${i}`)
  }
}

export function clearSessionFromResponse(
  response: NextResponse,
  configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig,
): void {
  const cfg = resolveOidcConfig(configInput)
  const name = resolveCookieName(cfg.session.name, cfg.cookie.secure)
  response.cookies.delete(name)
  for (let i = 0; i < cfg.cookie.maxChunks; i++) {
    response.cookies.delete(`${name}.${i}`)
  }
}

/**
 * Server Component / Server Action / Route Handler helper to get current session.
 */
export async function getHuiaSession(configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig): Promise<UserSession> {
  try {
    const cfg = resolveOidcConfig(configInput)
    const cookieStore = await cookies()
    const payload = await readSessionFromCookies(name => cookieStore.get(name)?.value, cfg)
    if (!payload || !payload.user) {
      return { user: null, loggedIn: false }
    }
    return {
      user: payload.user,
      loggedIn: true,
      expiresAt: payload.expiresAt,
    }
  }
  catch {
    return { user: null, loggedIn: false }
  }
}

/**
 * Server helper to get a fresh bearer access token for upstream API requests.
 */
export async function getAccessToken(configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig): Promise<string | null> {
  const cfg = resolveOidcConfig(configInput)
  const cookieStore = await cookies()
  const payload = await readSessionFromCookies(name => cookieStore.get(name)?.value, cfg)
  if (!payload?.sid) return null

  const record = await cfg.storage.getTokenRecord(payload.sid)
  if (!record) return null

  const freshRecord = await ensureFreshTokenRecord(
    cfg.storage,
    record,
    async (rec: TokenRecord) => {
      if (!rec.refreshToken) throw new Error('no_refresh_token')
      const oidcConfig = await defaultOidcHelper.getConfiguration(cfg)
      const res = await defaultOidcHelper.refreshTokens(oidcConfig, rec.refreshToken, rec.scope)
      const now = Date.now()

      return {
        ...rec,
        accessToken: res.access_token,
        refreshToken: res.refresh_token ?? rec.refreshToken,
        idToken: res.id_token ?? rec.idToken,
        claims: res.id_token ? ((res.claims() as never) ?? rec.claims) : rec.claims,
        accessTokenExpiresAt: now + (Number(res.expires_in) || 300) * 1000,
        updatedAt: now,
      }
    },
  )

  return freshRecord.accessToken
}
