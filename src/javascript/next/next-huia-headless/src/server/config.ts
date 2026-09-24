import { defaultStorage } from 'huia-auth-core'
import type { HuiaHeadlessConfig, ResolvedHuiaHeadlessConfig } from '../types.js'

export function resolveHeadlessConfig(config: HuiaHeadlessConfig | ResolvedHuiaHeadlessConfig): ResolvedHuiaHeadlessConfig {
  if ('routes' in config && config.routes && typeof config.baseUrl === 'string' && 'storage' in config && config.storage) {
    return config as ResolvedHuiaHeadlessConfig
  }

  const raw = config as HuiaHeadlessConfig
  if (!raw.baseUrl) {
    throw new Error('[next-huia-headless] `baseUrl` is required.')
  }

  const isProd = process.env.NODE_ENV === 'production'
  const secure = raw.cookie?.secure ?? (process.env.HUIA_COOKIE_SECURE === 'true')

  return {
    baseUrl: raw.baseUrl.replace(/\/+$/, ''),
    appUrl: raw.appUrl?.replace(/\/+$/, ''),
    session: {
      name: raw.session.name ?? (secure ? '__Host-huia_headless_sess' : 'huia_headless_sess'),
      password: raw.session.password,
      maxAge: raw.session.maxAge ?? 604800,
      userClaims: raw.session.userClaims ?? ['sub', 'name', 'email', 'firstName', 'lastName', 'roles', 'phoneNumber'],
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
      register: raw.routes?.register ?? '/api/auth/register',
      logout: raw.routes?.logout ?? '/api/auth/logout',
      session: raw.routes?.session ?? '/api/auth/session',
      refresh: raw.routes?.refresh ?? '/api/auth/refresh',
      phoneStart: raw.routes?.phoneStart ?? '/api/auth/phone/start',
      phoneVerify: raw.routes?.phoneVerify ?? '/api/auth/phone/verify',
      phoneCompleteProfile: raw.routes?.phoneCompleteProfile ?? '/api/auth/phone/complete-profile',
      externalLogin: raw.routes?.externalLogin ?? '/api/auth/external',
      externalExchange: raw.routes?.externalExchange ?? '/api/auth/external/exchange',
      externalCompleteProfile: raw.routes?.externalCompleteProfile ?? '/api/auth/external/complete-profile',
    },
    storage: raw.storage ?? defaultStorage,
    allowInsecureTls: raw.allowInsecureTls ?? !isProd,
  }
}
