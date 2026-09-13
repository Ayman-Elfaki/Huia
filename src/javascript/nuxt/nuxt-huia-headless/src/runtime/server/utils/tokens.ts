import { pickUserClaims as corePickClaims, sanitizeReturnTo } from 'huia-auth-core'
import type { UserClaims } from '../../types.js'
import type { BackendMeResponse } from './internal-types.js'

export { sanitizeReturnTo }

export function pickUserClaims(me: BackendMeResponse, whitelist: string[]): UserClaims {
  return corePickClaims(me as unknown as Record<string, unknown>, whitelist) as UserClaims
}
