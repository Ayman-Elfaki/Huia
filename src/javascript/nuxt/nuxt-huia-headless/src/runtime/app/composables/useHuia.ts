import { useRequestFetch, useRuntimeConfig } from '#imports'
import { useUserSession } from './useUserSession'
import type { UserClaims } from '../../types'

export interface HuiaLoginResult {
  ok: boolean
  user?: UserClaims
  /** The raw problem-details body from Huia.Headless when `ok` is false. */
  error?: unknown
}

/** Either a completed sign-in, or a first-time sign-up still needing a first/last name. */
export type HuiaFlowResult =
  | { ok: true, requiresProfile: false, user: UserClaims }
  | { ok: true, requiresProfile: true, flowId: string, email?: string | null, firstName?: string | null, lastName?: string | null }
  | { ok: false, requiresProfile: false, error: unknown }

export function useHuia() {
  const { user, loggedIn, session, hasRole, hasAnyRole, fetch: fetchSession, clear } = useUserSession()
  const paths = useRuntimeConfig().public.huiaHeadless as {
    registerPath: string
    loginPath: string
    logoutPath: string
    phoneStartPath: string
    phoneVerifyPath: string
    phoneCompleteProfilePath: string
    externalLoginPath: string
    externalExchangePath: string
    externalCompleteProfilePath: string
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

    /** Sends a one-time SMS code and returns an opaque `flowId` — pass it to `verifyPhoneLogin`. */
    async startPhoneLogin(opts: { phoneNumber: string, country?: string, captchaResponse?: string }): Promise<{ ok: boolean, flowId?: string, error?: unknown }> {
      try {
        const result = await useRequestFetch()(paths.phoneStartPath, { method: 'POST', body: opts }) as { flowId: string }
        return { ok: true, flowId: result.flowId }
      }
      catch (err) {
        return { ok: false, error: errorData(err) }
      }
    },

    /** Verifies a phone one-time code. Signs in directly, or reports a first-time sign-up needs a name. */
    async verifyPhoneLogin(opts: { flowId: string, code: string }): Promise<HuiaFlowResult> {
      try {
        const result = await useRequestFetch()(paths.phoneVerifyPath, { method: 'POST', body: opts }) as
          { requiresProfile: true, flowId: string } | { user: UserClaims }
        if ('requiresProfile' in result) {
          return { ok: true, requiresProfile: true, flowId: result.flowId }
        }
        await fetchSession()
        return { ok: true, requiresProfile: false, user: result.user }
      }
      catch (err) {
        return { ok: false, requiresProfile: false, error: errorData(err) }
      }
    },

    /** Completes a first-time phone sign-up with a name, then signs in. */
    async completePhoneProfile(opts: { flowId: string, firstName: string, lastName: string }): Promise<HuiaLoginResult> {
      try {
        const result = await useRequestFetch()(paths.phoneCompleteProfilePath, { method: 'POST', body: opts }) as { user: UserClaims }
        await fetchSession()
        return { ok: true, user: result.user }
      }
      catch (err) {
        return { ok: false, error: errorData(err) }
      }
    },

    /**
     * The URL to navigate the browser to (a plain link, not a fetch call) to start signing in with
     * `provider` (must match a name configured on the Huia.Headless tenant). `returnTo` is a
     * same-origin path within this app, resolved once the provider round trip completes.
     */
    externalLoginHref(provider: string, returnTo = '/'): string {
      return `${paths.externalLoginPath}/${encodeURIComponent(provider)}?returnUrl=${encodeURIComponent(returnTo)}`
    },

    /** Exchanges the one-time `code` from the callback URL. Signs in directly, or needs a name for a new sign-up. */
    async exchangeExternalCode(code: string): Promise<HuiaFlowResult> {
      try {
        const result = await useRequestFetch()(paths.externalExchangePath, { method: 'POST', body: { code } }) as
          | { requiresProfile: true, code: string, email: string | null, firstName: string | null, lastName: string | null }
          | { user: UserClaims }
        if ('requiresProfile' in result) {
          return { ok: true, requiresProfile: true, flowId: result.code, email: result.email, firstName: result.firstName, lastName: result.lastName }
        }
        await fetchSession()
        return { ok: true, requiresProfile: false, user: result.user }
      }
      catch (err) {
        return { ok: false, requiresProfile: false, error: errorData(err) }
      }
    },

    /** Completes a first-time external sign-up with a name, then signs in. */
    async completeExternalProfile(opts: { code: string, firstName: string, lastName: string }): Promise<HuiaLoginResult> {
      try {
        const result = await useRequestFetch()(paths.externalCompleteProfilePath, { method: 'POST', body: opts }) as { user: UserClaims }
        await fetchSession()
        return { ok: true, user: result.user }
      }
      catch (err) {
        return { ok: false, error: errorData(err) }
      }
    },
  }
}

function errorData(err: unknown): unknown {
  return (err as { data?: unknown } | undefined)?.data ?? err
}
