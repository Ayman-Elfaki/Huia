import { createServerFn } from '@tanstack/react-start'
import { getHuiaHeadlessSession, getHeadlessAccessToken } from './session.js'
import type { HuiaHeadlessConfig } from '../types.js'

/**
 * Creates type-safe TanStack Start server functions for fetching the current user session
 * and access tokens in headless mode.
 */
export function createHuiaHeadlessServerFns(config: HuiaHeadlessConfig) {
  const getSession = createServerFn({ method: 'GET' })
    .handler(async () => {
      return await getHuiaHeadlessSession(config)
    })

  const getBearerToken = createServerFn({ method: 'GET' })
    .handler(async () => {
      return await getHeadlessAccessToken(config)
    })

  return {
    getSession,
    getAccessToken: getBearerToken,
  }
}
