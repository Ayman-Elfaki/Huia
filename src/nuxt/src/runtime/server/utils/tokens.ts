import type { UserClaims } from '../../types'

/** Copy only the whitelisted keys out of the full id_token claim set. `sub` is always kept. */
export function pickUserClaims(claims: Record<string, unknown>, whitelist: string[]): UserClaims {
  const out: Record<string, unknown> = { sub: String(claims.sub ?? '') }
  for (const key of whitelist) {
    if (key === 'sub') continue
    if (claims[key] !== undefined) out[key] = claims[key]
  }
  return out as UserClaims
}

/** Throw unless the id_token issuer is exactly the resolved per-tenant issuer string. */
export function assertHuiaIssuer(iss: unknown, expected: string): void {
  if (iss !== expected) {
    throw new Error(`issuer_mismatch: id_token iss "${String(iss)}" !== "${expected}"`)
  }
}

/** True when a JWT's `exp` is in the past (with a small negative skew tolerance). */
export function isJwtExpired(jwt: string, skewSeconds = 30): boolean {
  try {
    const payload = jwt.split('.')[1]
    if (!payload) return true
    const json = JSON.parse(
      Buffer.from(payload.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8'),
    ) as { exp?: number }
    return typeof json.exp === 'number' && json.exp * 1000 < Date.now() - skewSeconds * 1000
  }
  catch {
    return true
  }
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
