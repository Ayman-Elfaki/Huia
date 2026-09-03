import { parseCookies, setCookie, deleteCookie, createError, type H3Event } from 'h3'
import uncrypto from 'uncrypto'
import { seal, unseal, defaults as ironDefaults } from 'iron-webcrypto'
import type { ResolvedAuthConfig, CookiePayload } from './internal-types'

// iron-webcrypto's internal `_Crypto` shape; uncrypto satisfies it at runtime.
const ironCrypto = uncrypto as unknown as Parameters<typeof seal>[0]

function sealOpts(cfg: ResolvedAuthConfig) {
  return { ...ironDefaults, ttl: cfg.session.maxAge * 1000 }
}

export function sealValue(cfg: ResolvedAuthConfig, value: unknown): Promise<string> {
  return seal(ironCrypto, value, cfg.session.password, sealOpts(cfg))
}

export async function unsealValue<T>(cfg: ResolvedAuthConfig, sealed: string | undefined): Promise<T | null> {
  if (!sealed) return null
  try {
    return (await unseal(ironCrypto, sealed, cfg.session.password, sealOpts(cfg))) as T
  }
  catch {
    return null
  }
}

/**
 * `__Host-` requires Secure + Path=/ + no Domain. Over plain http (dev only) that cannot hold, so
 * downgrade `__Host-` / `__Secure-` to the bare name.
 */
function baseName(cfg: ResolvedAuthConfig): string {
  if (cfg.secure) return cfg.session.name
  return cfg.session.name.replace(/^__Host-/, '').replace(/^__Secure-/, '')
}

function cookieOpts(cfg: ResolvedAuthConfig) {
  return {
    httpOnly: true,
    secure: cfg.secure,
    sameSite: 'lax' as const,
    path: '/',
    maxAge: cfg.session.maxAge,
  }
}

function countChunks(event: H3Event, name: string, max: number): number {
  const cookies = parseCookies(event)
  if (cookies[name] !== undefined) return 1
  let n = 0
  while (n < max && cookies[`${name}.${n}`] !== undefined) n++
  return n
}

function clearChunksFrom(event: H3Event, cfg: ResolvedAuthConfig, name: string, from: number): void {
  const cookies = parseCookies(event)
  for (let i = from; i < cfg.cookie.maxChunks; i++) {
    if (cookies[`${name}.${i}`] !== undefined) deleteCookie(event, `${name}.${i}`, { path: '/' })
  }
}

/* ── WRITE ──────────────────────────────────────────────────────────────────── */
export async function writeSessionCookie(event: H3Event, cfg: ResolvedAuthConfig, payload: CookiePayload): Promise<void> {
  const sealed = await sealValue(cfg, payload)
  const name = baseName(cfg)
  const limit = cfg.cookie.chunkSize
  const prevCount = countChunks(event, name, cfg.cookie.maxChunks)

  if (sealed.length <= limit && prevCount <= 1) {
    setCookie(event, name, sealed, cookieOpts(cfg))
    clearChunksFrom(event, cfg, name, 0)
    return
  }

  const parts: string[] = []
  for (let i = 0; i < sealed.length; i += limit) parts.push(sealed.slice(i, i + limit))

  if (parts.length > cfg.cookie.maxChunks) {
    throw createError({
      statusCode: 500,
      statusMessage: 'session_cookie_too_large',
      message: `Sealed session is ${sealed.length}B (> ${cfg.cookie.maxChunks} x ${limit}). `
        + 'Trim huiaAuth.session.userClaims or raise huiaAuth.cookie.maxChunks.',
    })
  }

  parts.forEach((part, i) => setCookie(event, `${name}.${i}`, part, cookieOpts(cfg)))
  deleteCookie(event, name, { path: '/' })
  clearChunksFrom(event, cfg, name, parts.length)
}

/* ── READ ───────────────────────────────────────────────────────────────────── */
export async function readSessionCookie(event: H3Event, cfg: ResolvedAuthConfig): Promise<CookiePayload | null> {
  const name = baseName(cfg)
  const cookies = parseCookies(event)

  const unchunked = cookies[name]
  if (unchunked !== undefined) return unsealValue<CookiePayload>(cfg, unchunked)

  const parts: string[] = []
  for (let i = 0; i < cfg.cookie.maxChunks; i++) {
    const v = cookies[`${name}.${i}`]
    if (v === undefined) break
    parts.push(v)
  }
  if (parts.length === 0) return null

  return unsealValue<CookiePayload>(cfg, parts.join(''))
}

/* ── CLEAR ──────────────────────────────────────────────────────────────────── */
export async function clearSessionCookies(event: H3Event, cfg: ResolvedAuthConfig): Promise<void> {
  const name = baseName(cfg)
  deleteCookie(event, name, { path: '/' })
  clearChunksFrom(event, cfg, name, 0)
}
