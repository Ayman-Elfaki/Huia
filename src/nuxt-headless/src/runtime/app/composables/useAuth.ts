import { useRequestFetch, useRuntimeConfig } from '#imports'
import { useUserSession } from './useUserSession'
import type { UserSession, PhoneLoginStartResponse, PhoneLoginVerifyResponse } from '../../types'

export function useAuth() {
  const rc = useRuntimeConfig()
  const paths = (rc.public.huiaHeadless ?? {}) as {
    loginPath?: string
    registerPath?: string
    logoutPath?: string
    phoneStartPath?: string
    phoneVerifyPath?: string
    phoneCompleteProfilePath?: string
  }

  const { fetch: refreshSession, setSession, clearLocal } = useUserSession()
  const $fetch = useRequestFetch()

  return {
    async login(payload: { email: string, password: string, twoFactorCode?: string, twoFactorRecoveryCode?: string }) {
      const res = await $fetch<{ ok?: boolean, requiresTwoFactor?: boolean, session?: UserSession }>(
        paths.loginPath || '/auth/headless/login',
        {
          method: 'POST',
          body: payload,
        }
      )

      if (res.session) {
        setSession(res.session)
      }

      return res
    },

    async register(payload: { email: string, password: string }) {
      return await $fetch<{ ok: boolean, message?: string }>(
        paths.registerPath || '/auth/headless/register',
        {
          method: 'POST',
          body: payload,
        }
      )
    },

    async logout() {
      try {
        await $fetch(paths.logoutPath || '/auth/headless/logout', { method: 'POST' })
      }
      finally {
        clearLocal()
      }
    },

    async phoneLoginStart(payload: { phoneNumber: string, captchaToken?: string }) {
      return await $fetch<PhoneLoginStartResponse>(
        paths.phoneStartPath || '/auth/headless/phone/login/start',
        {
          method: 'POST',
          body: payload,
        }
      )
    },

    async phoneLoginVerify(payload: { phoneNumber: string, code: string }) {
      const res = await $fetch<PhoneLoginVerifyResponse & { ok?: boolean, session?: UserSession }>(
        paths.phoneVerifyPath || '/auth/headless/phone/login/verify',
        {
          method: 'POST',
          body: payload,
        }
      )

      if (res.session) {
        setSession(res.session)
      }

      return res
    },

    async phoneCompleteProfile(payload: { provisionalToken: string, firstName: string, lastName: string }) {
      const res = await $fetch<{ ok?: boolean, session?: UserSession }>(
        paths.phoneCompleteProfilePath || '/auth/headless/phone/complete-profile',
        {
          method: 'POST',
          body: payload,
        }
      )

      if (res.session) {
        setSession(res.session)
      }

      return res
    },

    refreshSession,
  }
}
