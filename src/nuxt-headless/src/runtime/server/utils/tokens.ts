import type { UserClaims, HuiaTokenResponse } from '../../types'
import type { ResolvedHeadlessConfig, TokenRecord } from './internal-types'
import { newSessionId } from './storage'

export function decodeJwtPayload(token: string): Record<string, unknown> {
  const parts = token.split('.')
  if (parts.length !== 3) {
    throw new Error('Invalid JWT format')
  }
  const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/')
  const json = Buffer.from(base64, 'base64').toString('utf8')
  return JSON.parse(json) as Record<string, unknown>
}

export function extractUserClaims(claims: Record<string, unknown>, allowedKeys: string[]): UserClaims {
  const sub = String(claims.sub || claims.nameid || '')
  const result: UserClaims = { sub }

  for (const key of allowedKeys) {
    if (claims[key] !== undefined) {
      result[key] = claims[key]
    }
  }

  if (claims.role && !result.roles) {
    result.roles = Array.isArray(claims.role) ? claims.role.map(String) : [String(claims.role)]
  }

  return result
}

export function createTokenRecordFromResponse(
  tokens: HuiaTokenResponse,
  sid?: string
): TokenRecord {
  const claims = decodeJwtPayload(tokens.accessToken)
  const now = Date.now()
  const expSec = typeof claims.exp === 'number' ? claims.exp : Math.floor(now / 1000) + tokens.expiresIn

  return {
    sid: sid || newSessionId(),
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    accessTokenExpiresAt: expSec * 1000,
    refreshTokenExpiresAt: now + 30 * 24 * 60 * 60 * 1000,
    claims,
  }
}
