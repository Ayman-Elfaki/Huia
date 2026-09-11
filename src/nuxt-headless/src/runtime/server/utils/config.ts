import { useRuntimeConfig } from 'nitropack/runtime'
import { getRequestProtocol, type H3Event } from 'h3'
import type { ResolvedHeadlessConfig } from './internal-types'

export function useHeadlessConfig(event?: H3Event): ResolvedHeadlessConfig {
  const rc = useRuntimeConfig()
  const raw = (rc.huiaHeadless ?? {}) as Record<string, unknown>
  const huia = (raw.huia ?? {}) as Record<string, unknown>
  const session = (raw.session ?? {}) as Record<string, unknown>
  const cookie = (raw.cookie ?? {}) as Record<string, unknown>
  const refresh = (raw.refresh ?? {}) as Record<string, unknown>
  const routes = (raw.routes ?? {}) as Record<string, unknown>

  const isHttps = event ? getRequestProtocol(event, { xForwardedHost: true }) === 'https' : true

  return {
    huia: {
      baseUrl: String(huia.baseUrl || '').replace(/\/+$/, ''),
      tenant: String(huia.tenant || ''),
    },
    session: {
      name: String(session.name || '__Host-huia_headless_sess'),
      password: String(session.password || process.env.NUXT_HUIA_HEADLESS_SESSION_PASSWORD || ''),
      maxAge: Number(session.maxAge || 60 * 60 * 24 * 7),
      cookie: {
        sameSite: 'lax',
        secure: isHttps,
      },
      userClaims: Array.isArray(session.userClaims)
        ? (session.userClaims as string[])
        : ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
    },
    storageBase: String(raw.storageBase || 'huia-headless-auth'),
    refresh: {
      enabled: refresh.enabled !== false,
      earlyRefreshSeconds: Number(refresh.earlyRefreshSeconds || 60),
      lock: {
        ttlMs: 10_000,
        waitMs: 8_000,
        pollMs: 150,
      },
    },
    cookie: {
      chunkSize: Number(cookie.chunkSize || 3800),
      maxChunks: Number(cookie.maxChunks || 8),
    },
    routes: {
      register: String(routes.register || '/auth/headless/register'),
      login: String(routes.login || '/auth/headless/login'),
      logout: String(routes.logout || '/auth/headless/logout'),
      refresh: String(routes.refresh || '/auth/headless/refresh'),
      phoneStart: String(routes.phoneStart || '/auth/headless/phone/login/start'),
      phoneVerify: String(routes.phoneVerify || '/auth/headless/phone/login/verify'),
      phoneCompleteProfile: String(routes.phoneCompleteProfile || '/auth/headless/phone/complete-profile'),
      session: String(routes.session || '/api/_auth/session'),
    },
    secure: isHttps,
  }
}
