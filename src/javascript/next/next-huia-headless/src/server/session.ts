import { cookies } from 'next/headers'
import { NextResponse } from 'next/server'
import {
  sealValue,
  unsealValue,
  splitIntoChunks,
  assembleChunks,
  resolveCookieName,
  ensureFreshTokenRecord,
  pickUserClaims,
  HuiaHeadlessClient,
  type CookiePayload,
  type UserSession,
  type TokenRecord,
} from 'huia-auth-core'
import { resolveHeadlessConfig } from './config.js'
import type { HuiaHeadlessConfig, ResolvedHuiaHeadlessConfig } from '../types.js'

export async function readSessionFromCookies(
  cookieGetter: (name: string) => string | undefined,
  configInput: HuiaHeadlessConfig | ResolvedHuiaHeadlessConfig,
): Promise<CookiePayload | null> {
  const cfg = resolveHeadlessConfig(configInput)
  const name = resolveCookieName(cfg.session.name, cfg.cookie.secure)
  const sealed = assembleChunks(cookieGetter, name, cfg.cookie.maxChunks)
  if (!sealed) return null
  return unsealValue<CookiePayload>(cfg.session.password, sealed, cfg.session.maxAge)
}

export async function writeSessionToResponse(
  response: NextResponse,
  configInput: HuiaHeadlessConfig | ResolvedHuiaHeadlessConfig,
  payload: CookiePayload,
): Promise<void> {
  const cfg = resolveHeadlessConfig(configInput)
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
    for (let i = 0; i < cfg.cookie.maxChunks; i++) {
      response.cookies.delete(`${name}.${i}`)
    }
    return
  }

  const chunks = splitIntoChunks(sealed, limit)
  if (chunks.length > cfg.cookie.maxChunks) {
    throw new Error(`[next-huia-headless] Session cookie exceeds maximum chunk count (${chunks.length} > ${cfg.cookie.maxChunks}).`)
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
  configInput: HuiaHeadlessConfig | ResolvedHuiaHeadlessConfig,
): void {
  const cfg = resolveHeadlessConfig(configInput)
  const name = resolveCookieName(cfg.session.name, cfg.cookie.secure)
  response.cookies.delete(name)
  for (let i = 0; i < cfg.cookie.maxChunks; i++) {
    response.cookies.delete(`${name}.${i}`)
  }
}

/**
 * Server Component / Server Action / Route Handler helper to get current session.
 */
export async function getHuiaHeadlessSession(configInput: HuiaHeadlessConfig | ResolvedHuiaHeadlessConfig): Promise<UserSession> {
  try {
    const cfg = resolveHeadlessConfig(configInput)
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
export async function getAccessToken(configInput: HuiaHeadlessConfig | ResolvedHuiaHeadlessConfig): Promise<string | null> {
  const cfg = resolveHeadlessConfig(configInput)
  const cookieStore = await cookies()
  const payload = await readSessionFromCookies(name => cookieStore.get(name)?.value, cfg)
  if (!payload?.sid) return null

  const record = await cfg.storage.getTokenRecord(payload.sid)
  if (!record) return null

  const client = new HuiaHeadlessClient({
    baseUrl: cfg.baseUrl,
    allowInsecureTls: cfg.allowInsecureTls,
  })

  const freshRecord = await ensureFreshTokenRecord(
    cfg.storage,
    record,
    async (rec: TokenRecord) => {
      if (!rec.refreshToken) throw new Error('no_refresh_token')
      const res = await client.refresh(rec.refreshToken)
      const me = await client.me(res.accessToken)
      const now = Date.now()

      return {
        ...rec,
        accessToken: res.accessToken,
        refreshToken: res.refreshToken ?? rec.refreshToken,
        claims: pickUserClaims(me as unknown as Record<string, unknown>, cfg.session.userClaims),
        accessTokenExpiresAt: now + (res.expiresIn || 300) * 1000,
        updatedAt: now,
      }
    },
  )

  return freshRecord.accessToken
}
