import { createMiddleware } from '@tanstack/react-start'
import { redirect } from '@tanstack/react-router'
import { getHuiaHeadlessSession } from './session.js'
import { resolveHeadlessConfig } from './config.js'
import type { HuiaHeadlessConfig } from '../types.js'

export function createHuiaHeadlessAuthMiddleware(configInput: HuiaHeadlessConfig) {
  const cfg = resolveHeadlessConfig(configInput)

  return createMiddleware({ type: 'function' }).server(async ({ next }) => {
    const session = await getHuiaHeadlessSession(cfg)
    return next({
      context: {
        session,
      },
    })
  })
}

export interface RequireHuiaHeadlessAuthOptions {
  config: HuiaHeadlessConfig
  loginUrl?: string
  returnTo?: string
  request?: Request
}

export async function requireHuiaHeadlessAuth(options: RequireHuiaHeadlessAuthOptions) {
  const cfg = resolveHeadlessConfig(options.config)
  const session = await getHuiaHeadlessSession(cfg, options.request)

  if (!session.loggedIn || !session.user) {
    const loginUrl = options.loginUrl ?? '/login'
    const search = options.returnTo ? `?returnTo=${encodeURIComponent(options.returnTo)}` : ''
    throw redirect({
      href: `${loginUrl}${search}`,
    })
  }

  return session
}
