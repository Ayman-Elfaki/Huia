import { createMiddleware } from '@tanstack/react-start'
import { redirect } from '@tanstack/react-router'
import { getHuiaSession } from './session.js'
import { resolveOidcConfig } from './config.js'
import type { HuiaOidcConfig } from '../types.js'

/**
 * Creates a TanStack Start function middleware that retrieves the active user session
 * and injects it into `context.session`.
 */
export function createHuiaAuthMiddleware(configInput: HuiaOidcConfig) {
  const cfg = resolveOidcConfig(configInput)

  return createMiddleware({ type: 'function' }).server(async ({ next }) => {
    const session = await getHuiaSession(cfg)
    return next({
      context: {
        session,
      },
    })
  })
}

export interface RequireHuiaAuthOptions {
  config: HuiaOidcConfig
  loginUrl?: string
  returnTo?: string
  request?: Request
}

/**
 * Helper to enforce authentication inside TanStack Router route definitions (e.g. `beforeLoad`).
 * If the user is not authenticated, throws a router `redirect` to the login route with `returnTo`.
 *
 * @example
 * ```ts
 * export const Route = createFileRoute('/dashboard')({
 *   beforeLoad: async ({ location }) => {
 *     return await requireHuiaAuth({ config: huiaConfig, returnTo: location.href })
 *   },
 *   component: DashboardComponent,
 * })
 * ```
 */
export async function requireHuiaAuth(options: RequireHuiaAuthOptions) {
  const cfg = resolveOidcConfig(options.config)
  const session = await getHuiaSession(cfg, options.request)

  if (!session.loggedIn || !session.user) {
    const loginUrl = options.loginUrl ?? cfg.routes.login
    const search = options.returnTo ? `?returnTo=${encodeURIComponent(options.returnTo)}` : ''
    throw redirect({
      href: `${loginUrl}${search}`,
    })
  }

  return session
}
