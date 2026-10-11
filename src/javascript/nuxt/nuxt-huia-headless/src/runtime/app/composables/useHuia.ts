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
  | { ok: true, requiresProfile: true, flowId: string, maskedPhoneNumber?: string, email?: string | null, firstName?: string | null, lastName?: string | null }
  | { ok: false, requiresProfile: false, error: unknown }

/**
 * What `startPhoneLogin` / `resendPhoneLogin` return. On success everything a code-entry screen needs;
 * on failure `error` is the backend problem, `{ error: 'rate_limited' | 'daily_limit' | 'resend_too_soon' | 'invalid' | ...,
 * retryAfterSeconds? }`, or a `{ errors }` validation body.
 */
export interface HuiaPhoneCodeResult {
  ok: boolean
  flowId?: string
  /** The destination number, masked for display. */
  maskedPhoneNumber?: string
  /** How many digits the code has. */
  codeLength?: number
  /** How long the code stays valid. */
  expiresInSeconds?: number
  /** How long until `resendPhoneLogin` is accepted. */
  resendAvailableInSeconds?: number
  error?: unknown
}

export function useHuia() {
  const { user, loggedIn, session, hasRole, hasAnyRole, fetch: fetchSession, clear } = useUserSession()
  const paths = useRuntimeConfig().public.huiaHeadless as {
    registerPath: string
    loginPath: string
    logoutPath: string
    phoneStartPath: string
    phoneResendPath: string
    phoneVerifyPath: string
    phoneCompleteProfilePath: string
    externalLoginPath: string
    externalExchangePath: string
    externalCompleteProfilePath: string
  }

  async function sendPhoneCode(path: string, body: Record<string, unknown>): Promise<HuiaPhoneCodeResult> {
    try {
      const result = await useRequestFetch()(path, { method: 'POST', body }) as {
        flowId: string
        maskedPhoneNumber: string
        codeLength: number
        expiresInSeconds: number
        resendAvailableInSeconds: number
      }
      return { ok: true, ...result }
    }
    catch (err) {
      return { ok: false, error: problemData(err) }
    }
  }

  return {
    user,
    loggedIn,
    session,
    hasRole,
    hasAnyRole,

    /** Creates an account. Does not sign in — call `login()` afterward (once any required email confirmation is done). */
    async register(opts: { email: string, password: string, firstName: string, lastName: string, phoneNumber?: string, country?: string }): Promise<{ ok: boolean, error?: unknown }> {
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

    /**
     * Sends a one-time SMS code and returns an opaque `flowId` — pass it to `verifyPhoneLogin` — along with
     * what a code-entry screen needs: the masked number, the code length, and the expiry / resend windows.
     * On failure `error` carries the backend's `{ error, retryAfterSeconds? }` problem.
     */
    async startPhoneLogin(opts: { phoneNumber: string, country?: string, captchaResponse?: string }): Promise<HuiaPhoneCodeResult> {
      return sendPhoneCode(paths.phoneStartPath, opts)
    },

    /**
     * Sends a fresh code for an in-progress flow. The backend enforces the resend cooldown: too early is
     * `{ ok: false, error: { error: 'resend_too_soon', retryAfterSeconds } }`.
     */
    async resendPhoneLogin(opts: { flowId: string, captchaResponse?: string }): Promise<HuiaPhoneCodeResult> {
      return sendPhoneCode(paths.phoneResendPath, opts)
    },

    /** Verifies a phone one-time code. Signs in directly, or reports a first-time sign-up needs a name. */
    async verifyPhoneLogin(opts: { flowId: string, code: string }): Promise<HuiaFlowResult> {
      try {
        const result = await useRequestFetch()(paths.phoneVerifyPath, { method: 'POST', body: opts }) as
          { requiresProfile: true, flowId: string, maskedPhoneNumber?: string } | { user: UserClaims }
        if ('requiresProfile' in result) {
          return { ok: true, requiresProfile: true, flowId: result.flowId, maskedPhoneNumber: result.maskedPhoneNumber }
        }
        await fetchSession()
        return { ok: true, requiresProfile: false, user: result.user }
      }
      catch (err) {
        return { ok: false, requiresProfile: false, error: problemData(err) }
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
        return { ok: false, error: problemData(err) }
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
    async completeExternalProfile(opts: { code: string, firstName: string, lastName: string, phoneNumber?: string, country?: string }): Promise<HuiaLoginResult> {
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

/** The backend problem body (`{ error, retryAfterSeconds?, attemptsRemaining? }`) behind a failed BFF call. */
function problemData(err: unknown): unknown {
  const body = (err as { data?: { data?: unknown } } | undefined)?.data
  return body?.data ?? body ?? err
}

function errorData(err: unknown): unknown {
  return (err as { data?: unknown } | undefined)?.data ?? err
}
