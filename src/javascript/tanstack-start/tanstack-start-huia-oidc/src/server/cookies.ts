import {
  sealValue,
  unsealValue,
  splitIntoChunks,
  assembleChunks,
  resolveCookieName,
  type CookiePayload,
} from 'huia-auth-core'
import { resolveOidcConfig } from './config.js'
import type { HuiaOidcConfig, ResolvedHuiaOidcConfig } from '../types.js'

export interface CookieSerializeOptions {
  maxAge?: number
  domain?: string
  path?: string
  expires?: Date
  httpOnly?: boolean
  secure?: boolean
  sameSite?: 'lax' | 'strict' | 'none'
}

export function parseCookieHeader(header?: string | null): Record<string, string> {
  if (!header) return {}
  const cookies: Record<string, string> = {}
  for (const pair of header.split(';')) {
    const idx = pair.indexOf('=')
    if (idx < 0) continue
    const key = pair.slice(0, idx).trim()
    const val = pair.slice(idx + 1).trim()
    if (key) {
      try {
        cookies[key] = decodeURIComponent(val)
      }
      catch {
        cookies[key] = val
      }
    }
  }
  return cookies
}

export function serializeSetCookie(name: string, value: string, options: CookieSerializeOptions = {}): string {
  let str = `${encodeURIComponent(name)}=${encodeURIComponent(value)}`
  if (options.maxAge !== undefined) str += `; Max-Age=${Math.floor(options.maxAge)}`
  if (options.domain) str += `; Domain=${options.domain}`
  if (options.path) str += `; Path=${options.path}`
  if (options.expires) str += `; Expires=${options.expires.toUTCString()}`
  if (options.httpOnly) str += '; HttpOnly'
  if (options.secure) str += '; Secure'
  if (options.sameSite) {
    const s = options.sameSite.toLowerCase()
    str += `; SameSite=${s.charAt(0).toUpperCase() + s.slice(1)}`
  }
  return str
}

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

export async function writeSessionToHeaders(
  headers: Headers,
  configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig,
  payload: CookiePayload,
): Promise<void> {
  const cfg = resolveOidcConfig(configInput)
  const sealed = await sealValue(cfg.session.password, payload, cfg.session.maxAge)
  const name = resolveCookieName(cfg.session.name, cfg.cookie.secure)
  const limit = cfg.cookie.chunkSize

  const cookieBase: CookieSerializeOptions = {
    httpOnly: true,
    secure: cfg.cookie.secure,
    sameSite: cfg.cookie.sameSite,
    path: cfg.cookie.path,
    maxAge: cfg.session.maxAge,
  }

  if (sealed.length <= limit) {
    headers.append('Set-Cookie', serializeSetCookie(name, sealed, cookieBase))
    for (let i = 0; i < cfg.cookie.maxChunks; i++) {
      headers.append('Set-Cookie', serializeSetCookie(`${name}.${i}`, '', { ...cookieBase, maxAge: 0 }))
    }
    return
  }

  const chunks = splitIntoChunks(sealed, limit)
  if (chunks.length > cfg.cookie.maxChunks) {
    throw new Error(`[@huia/tanstack-start-oidc] Session cookie exceeds maximum chunk count (${chunks.length} > ${cfg.cookie.maxChunks}).`)
  }

  headers.append('Set-Cookie', serializeSetCookie(name, '', { ...cookieBase, maxAge: 0 }))
  chunks.forEach((chunk, index) => {
    headers.append('Set-Cookie', serializeSetCookie(`${name}.${index}`, chunk, cookieBase))
  })
  for (let i = chunks.length; i < cfg.cookie.maxChunks; i++) {
    headers.append('Set-Cookie', serializeSetCookie(`${name}.${i}`, '', { ...cookieBase, maxAge: 0 }))
  }
}

export function clearSessionFromHeaders(
  headers: Headers,
  configInput: HuiaOidcConfig | ResolvedHuiaOidcConfig,
): void {
  const cfg = resolveOidcConfig(configInput)
  const name = resolveCookieName(cfg.session.name, cfg.cookie.secure)
  const cookieBase: CookieSerializeOptions = {
    httpOnly: true,
    secure: cfg.cookie.secure,
    sameSite: cfg.cookie.sameSite,
    path: cfg.cookie.path,
    maxAge: 0,
  }
  headers.append('Set-Cookie', serializeSetCookie(name, '', cookieBase))
  for (let i = 0; i < cfg.cookie.maxChunks; i++) {
    headers.append('Set-Cookie', serializeSetCookie(`${name}.${i}`, '', cookieBase))
  }
}
