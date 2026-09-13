import uncrypto from 'uncrypto'
import { seal, unseal, defaults as ironDefaults } from 'iron-webcrypto'

// iron-webcrypto's internal _Crypto shape; uncrypto satisfies it at runtime in node/browser/edge.
const ironCrypto = uncrypto as unknown as Parameters<typeof seal>[0]

export function splitIntoChunks(value: string, limit: number): string[] {
  if (limit <= 0) return [value]
  const parts: string[] = []
  for (let i = 0; i < value.length; i += limit) {
    parts.push(value.slice(i, i + limit))
  }
  return parts
}

export function sealValue(password: string, value: unknown, ttlSeconds = 604800): Promise<string> {
  return seal(ironCrypto, value, password, {
    ...ironDefaults,
    ttl: ttlSeconds * 1000,
  })
}

export async function unsealValue<T>(password: string, sealed: string | undefined, ttlSeconds = 604800): Promise<T | null> {
  if (!sealed) return null
  try {
    return (await unseal(ironCrypto, sealed, password, {
      ...ironDefaults,
      ttl: ttlSeconds * 1000,
    })) as T
  }
  catch {
    return null
  }
}

/**
 * Normalizes the cookie name:
 * If secure=true, preserves `__Host-` or `__Secure-` prefixes.
 * If secure=false (e.g. dev over plain http), strips `__Host-` and `__Secure-` prefixes
 * because browsers strictly reject prefixed cookies over insecure HTTP.
 */
export function resolveCookieName(name: string, secure: boolean): string {
  if (secure) return name
  return name.replace(/^__Host-/, '').replace(/^__Secure-/, '')
}

/**
 * Reassembles a chunked cookie if present, or reads single cookie.
 */
export function assembleChunks(
  getCookie: (name: string) => string | undefined,
  name: string,
  maxChunks = 4,
): string | null {
  const direct = getCookie(name)
  if (direct !== undefined && direct !== '') return direct

  const parts: string[] = []
  for (let i = 0; i < maxChunks; i++) {
    const part = getCookie(`${name}.${i}`)
    if (part === undefined || part === '') break
    parts.push(part)
  }

  return parts.length > 0 ? parts.join('') : null
}
