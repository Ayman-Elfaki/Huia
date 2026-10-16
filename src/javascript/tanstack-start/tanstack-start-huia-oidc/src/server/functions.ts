import { createServerFn } from '@tanstack/react-start'
import { getHuiaSession, getAccessToken } from './session.js'
import type { HuiaOidcConfig } from '../types.js'

/**
 * Creates type-safe TanStack Start server functions for fetching the current user session
 * and access tokens from loaders or client components.
 */
export function createHuiaServerFns(config: HuiaOidcConfig) {
  const getSession = createServerFn({ method: 'GET' })
    .handler(async () => {
      return await getHuiaSession(config)
    })

  const getBearerToken = createServerFn({ method: 'GET' })
    .handler(async () => {
      return await getAccessToken(config)
    })

  return {
    getSession,
    getAccessToken: getBearerToken,
  }
}
