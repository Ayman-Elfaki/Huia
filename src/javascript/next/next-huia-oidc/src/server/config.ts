import { defaultStorage } from 'huia-auth-core'
import type { HuiaOidcConfig, ResolvedHuiaOidcConfig } from '../types.js'

export function resolveOidcConfig(config: HuiaOidcConfig | ResolvedHuiaOidcConfig): ResolvedHuiaOidcConfig {
  if ('routes' in config && config.routes && typeof config.issuer === 'string' && 'storage' in config && config.storage) {
    return config as ResolvedHuiaOidcConfig
  }

  const raw = config as HuiaOidcConfig

  let issuer = raw.issuer
  if (!issuer) {
    if (raw.baseUrl && raw.tenant) {
      issuer = `${raw.baseUrl.replace(/\/+$/, '')}/${raw.tenant}`
    }
    else {
      throw new Error('[next-huia-oidc] Either `issuer` or both `baseUrl` and `tenant` must be provided.')
    }
  }
  issuer = issuer.replace(/\/+$/, '')

  const isProd = process.env.NODE_ENV === 'production'
  const secure = raw.cookie?.secure ?? (process.env.HUIA_COOKIE_SECURE === 'true')

  return {
    issuer,
    clientId: raw.clientId,
    clientSecret: raw.clientSecret,
    redirectUri: raw.redirectUri ?? '/api/auth/callback',
    appUrl: raw.appUrl?.replace(/\/+$/, ''),
    scopes: raw.scopes ?? ['openid', 'profile', 'email', 'roles', 'offline_access'],
    par: {
      enabled: raw.par?.enabled ?? true,
      required: raw.par?.required ?? false,
    },
    allowedAuthParams: raw.allowedAuthParams ?? ['ui_locales'],
    session: {
      name: raw.session.name ?? (secure ? '__Host-huia_sess' : 'huia_sess'),
      password: raw.session.password,
      maxAge: raw.session.maxAge ?? 604800,
      userClaims: raw.session.userClaims ?? ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
      stateless: raw.session.stateless ?? false,
    },
    cookie: {
      chunkSize: raw.cookie?.chunkSize ?? 3800,
      maxChunks: raw.cookie?.maxChunks ?? 4,
      secure,
      sameSite: raw.cookie?.sameSite ?? 'lax',
      path: raw.cookie?.path ?? '/',
    },
    routes: {
      login: raw.routes?.login ?? '/api/auth/login',
      callback: raw.routes?.callback ?? '/api/auth/callback',
      logout: raw.routes?.logout ?? '/api/auth/logout',
      session: raw.routes?.session ?? '/api/auth/session',
      error: raw.routes?.error ?? '/auth/error',
    },
    storage: raw.storage ?? defaultStorage,
    allowInsecureTls: raw.allowInsecureTls ?? !isProd,
  }
}
