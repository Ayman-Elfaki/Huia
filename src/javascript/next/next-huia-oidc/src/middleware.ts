import { NextResponse, type NextRequest } from 'next/server'
import { resolveCookieName } from 'huia-auth-core'
import type { HuiaOidcConfig } from './types.js'
import { resolveOidcConfig } from './server/config.js'

export function withHuiaOidcAuth(
  options: {
    protectedRoutes?: (string | RegExp)[]
    loginUrl?: string
    config: HuiaOidcConfig
  },
) {
  const cfg = resolveOidcConfig(options.config)
  const cookieName = resolveCookieName(cfg.session.name, cfg.cookie.secure)
  const loginUrl = options.loginUrl ?? cfg.routes.login

  return async function middleware(req: NextRequest): Promise<NextResponse | undefined> {
    const pathname = req.nextUrl.pathname
    const isProtected = (options.protectedRoutes ?? []).some(pattern => {
      if (typeof pattern === 'string') {
        return pathname === pattern || pathname.startsWith(`${pattern}/`)
      }
      return pattern.test(pathname)
    })

    if (!isProtected) {
      return NextResponse.next()
    }

    const hasSessionCookie = req.cookies.has(cookieName) || req.cookies.has(`${cookieName}.0`)
    if (!hasSessionCookie) {
      const redirectUrl = new URL(loginUrl, req.url)
      redirectUrl.searchParams.set('returnTo', pathname + req.nextUrl.search)
      return NextResponse.redirect(redirectUrl)
    }

    return NextResponse.next()
  }
}
