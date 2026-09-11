import { parseCookies, setCookie, deleteCookie, type H3Event } from 'h3'
import uncrypto from 'uncrypto'
import { seal, unseal, defaults as ironDefaults } from 'iron-webcrypto'
import type { ResolvedHeadlessConfig, CookiePayload } from './internal-types'

const ironCrypto = uncrypto as unknown as Parameters<typeof seal>[0]

function sealOpts(cfg: ResolvedHeadlessConfig) {
  return { ...ironDefaults, ttl: cfg.session.maxAge * 1000 }
}

export function splitIntoChunks(value: string, limit: number): string[] {
  const parts: string[] = []
  for (let i = 0; i < value.length; i += limit) parts.push(value.slice(i, i + limit))
  return parts
}

export function sealValue(cfg: ResolvedHeadlessConfig, value: unknown): Promise<string> {
  return seal(ironCrypto, value, cfg.session.password, sealOpts(cfg))
}

export async function unsealValue<T>(cfg: ResolvedHeadlessConfig, sealed: string | undefined): Promise<T | null> {
  if (!sealed) return null
  try {
    return (await unseal(ironCrypto, sealed, cfg.session.password, sealOpts(cfg))) as T
  }
  catch {
    return null
  }
}

function baseName(cfg: ResolvedHeadlessConfig): string {
  if (cfg.secure) return cfg.session.name
  return cfg.session.name.replace(/^__Host-/, '').replace(/^__Secure-/, '')
}

function cookieOpts(cfg: ResolvedHeadlessConfig) {
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

function clearChunksFrom(event: H3Event, cfg: ResolvedHeadlessConfig, name: string, from: number): void {
  const cookies = parseCookies(event)
  for (let i = from; i < cfg.cookie.maxChunks; i++) {
    if (cookies[`${name}.${i}`] !== undefined) deleteCookie(event, `${name}.${i}`, { path: '/' })
  }
}

export async function writeSessionCookie(event: H3Event, cfg: ResolvedHeadlessConfig, payload: CookiePayload): Promise<void> {
  const sealed = await sealValue(cfg, payload)
  const name = baseName(cfg)
  const chunks = splitIntoChunks(sealed, cfg.cookie.chunkSize)

  if (chunks.length > cfg.cookie.maxChunks) {
    throw new Error(`[huia-headless] Session exceeds maxChunks (${chunks.length} > ${cfg.cookie.maxChunks})`)
  }

  const prev = countChunks(event, name, cfg.cookie.maxChunks)
  const opts = cookieOpts(cfg)

  if (chunks.length === 1) {
    setCookie(event, name, chunks[0], opts)
    clearChunksFrom(event, cfg, name, 0)
  }
  else {
    deleteCookie(event, name, { path: '/' })
    for (let i = 0; i < chunks.length; i++) setCookie(event, `${name}.${i}`, chunks[i], opts)
    clearChunksFrom(event, cfg, name, chunks.length)
  }
}

export async function readSessionCookie(event: H3Event, cfg: ResolvedHeadlessConfig): Promise<CookiePayload | null> {
  const name = baseName(cfg)
  const cookies = parseCookies(event)
  let sealed: string | undefined

  if (cookies[name] !== undefined) {
    sealed = cookies[name]
  }
  else if (cookies[`${name}.0`] !== undefined) {
    const parts: string[] = []
    for (let i = 0; i < cfg.cookie.maxChunks; i++) {
      const p = cookies[`${name}.${i}`]
      if (p === undefined) break
      parts.push(p)
    }
    sealed = parts.join('')
  }

  return unsealValue<CookiePayload>(cfg, sealed)
}

export function clearSessionCookie(event: H3Event, cfg: ResolvedHeadlessConfig): void {
  const name = baseName(cfg)
  deleteCookie(event, name, { path: '/' })
  clearChunksFrom(event, cfg, name, 0)
}
