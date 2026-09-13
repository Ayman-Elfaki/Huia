import type { UserClaims } from '../../types'
import type { BackendMeResponse } from './internal-types'

/** Projects `identity/me`'s response onto the whitelisted claim set stored in the session cookie. */
export function pickUserClaims(me: BackendMeResponse, whitelist: string[]): UserClaims {
  const full: UserClaims = {
    sub: me.sub,
    email: me.email ?? undefined,
    firstName: me.firstName || undefined,
    lastName: me.lastName || undefined,
    roles: me.roles,
  }

  if (whitelist.length === 0) return full

  const out: Record<string, unknown> = { sub: me.sub }
  for (const key of whitelist) {
    if (key === 'sub') continue
    if (full[key] !== undefined) out[key] = full[key]
  }
  return out as UserClaims
}

/**
 * Accept only a same-origin path. Rejects absolute URLs, protocol-relative `//host`, backslash
 * tricks, whitespace and control characters. Returns `/` on rejection.
 */
export function sanitizeReturnTo(value: string | undefined | null): string {
  if (typeof value !== 'string' || value.length === 0) return '/'
  if (value[0] !== '/') return '/'
  if (value[1] === '/' || value[1] === '\\') return '/'
  for (let i = 0; i < value.length; i++) {
    const c = value.charCodeAt(i)
    if (c <= 0x20 || c === 0x7f) return '/'
  }
  return value
}
