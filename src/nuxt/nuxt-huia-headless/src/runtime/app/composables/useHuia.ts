import { useRequestFetch, useRuntimeConfig } from '#imports'
import { useUserSession } from './useUserSession'
import type { UserClaims } from '../../types'

export interface HuiaLoginResult {
  ok: boolean
  user?: UserClaims
  /** The raw problem-details body from Huia.Headless when `ok` is false. */
  error?: unknown
}

export function useHuia() {
  const { user, loggedIn, session, hasRole, hasAnyRole, fetch: fetchSession, clear } = useUserSession()
  const paths = useRuntimeConfig().public.huiaHeadless as {
    registerPath: string
    loginPath: string
    logoutPath: string
  }

  return {
    user,
    loggedIn,
    session,
    hasRole,
    hasAnyRole,

    /** Creates an account. Does not sign in — call `login()` afterward (once any required email confirmation is done). */
    async register(opts: { email: string, password: string }): Promise<{ ok: boolean, error?: unknown }> {
      try {
        await useRequestFetch()(paths.registerPath, { method: 'POST', body: opts })
        return { ok: true }
      }
      catch (err) {
        return { ok: false, error: errorData(err) }
      }
    },

    /** Signs in with email + password (and, when required, a `twoFactorCode`/`twoFactorRecoveryCode`). */
    async login(opts: { email: string, password: string, twoFactorCode?: string, twoFactorRecoveryCode?: string }): Promise<HuiaLoginResult> {
      try {
        const result = await useRequestFetch()(paths.loginPath, { method: 'POST', body: opts }) as { user: UserClaims }
        await fetchSession()
        return { ok: true, user: result.user }
      }
      catch (err) {
        return { ok: false, error: errorData(err) }
      }
    },

    /** Local logout — clears the session cookie and the stored tokens. Never redirects. */
    logout: clear,
  }
}

function errorData(err: unknown): unknown {
  return (err as { data?: unknown } | undefined)?.data ?? err
}
