import type { UserClaims } from './types.js'

/**
 * Copy only the whitelisted keys out of the claims set. `sub` is always kept.
 * Normalizes `role` (scalar or array) into `roles: string[]`.
 */
export function pickUserClaims(claims: Record<string, unknown>, whitelist?: string[]): UserClaims {
  const out: Record<string, unknown> = { sub: String(claims.sub ?? '') }
  const keys = whitelist && whitelist.length > 0 ? whitelist : Object.keys(claims)

  for (const key of keys) {
    if (key === 'sub') continue
    if (key === 'roles' || key === 'role') {
      const roleVal = claims.roles ?? claims.role
      if (roleVal !== undefined) {
        out.roles = Array.isArray(roleVal) ? roleVal.map(String) : [String(roleVal)]
      }
      continue
    }
    if (claims[key] !== undefined) {
      out[key] = claims[key]
    }
  }

  return out as UserClaims
}

/** Throw unless the id_token issuer is exactly the resolved per-tenant issuer string. */
export function assertHuiaIssuer(iss: unknown, expected: string): void {
  if (iss !== expected) {
    throw new Error(`issuer_mismatch: id_token iss "${String(iss)}" !== "${expected}"`)
  }
}

/** Check if token needs refresh given expiration time and buffer (default 30 seconds). */
export function needsRefresh(accessTokenExpiresAt: number, earlyRefreshSeconds = 30): boolean {
  return accessTokenExpiresAt - Date.now() < earlyRefreshSeconds * 1000
}

/** True when a JWT's `exp` is in the past (with a small negative skew tolerance). */
export function isJwtExpired(jwt: string, skewSeconds = 30): boolean {
  try {
    const payload = jwt.split('.')[1]
    if (!payload) return true
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/')
    const decoded = typeof atob === 'function'
      ? atob(base64)
      : Buffer.from(base64, 'base64').toString('utf8')
    const json = JSON.parse(decoded) as { exp?: number }
    return typeof json.exp === 'number' && json.exp * 1000 < Date.now() - skewSeconds * 1000
  }
  catch {
    return true
  }
}

/**
 * Accept only a same-origin relative path. Rejects absolute URLs, protocol-relative `//host`,
 * backslash tricks, whitespace and control characters. Returns `/` on rejection.
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
