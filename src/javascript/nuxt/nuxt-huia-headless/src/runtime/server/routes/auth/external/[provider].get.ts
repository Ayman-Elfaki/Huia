import { defineEventHandler, getRouterParam, getQuery, getRequestURL, sendRedirect, createError } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { sanitizeReturnTo } from '../../../utils/tokens'

/**
 * Starts an external-provider sign-in. Huia.Headless's own challenge endpoint needs an absolute
 * `returnUrl` it can redirect the browser back to once the provider round trip completes — and,
 * because that redirect lands on a *different* origin than Huia.Headless itself, Huia.Headless
 * validates it against an explicit allow-list (`ExternalLoginOptions.AllowReturnUrlPrefix`), not a
 * same-origin check. This route builds that absolute URL itself (the app's own origin +
 * `externalCallbackPath`, carrying the caller's `returnTo` along for the ride) so a caller only ever
 * has to pass a same-origin path, exactly like every other `returnTo`/`returnUrl` in this module.
 */
export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const provider = getRouterParam(event, 'provider')
  if (!provider) {
    throw createError({ statusCode: 400, statusMessage: 'missing_provider' })
  }

  const query = getQuery(event)
  const returnTo = sanitizeReturnTo(typeof query.returnUrl === 'string' ? query.returnUrl : undefined)

  const origin = getRequestURL(event).origin
  const callbackUrl = `${origin}${cfg.externalCallbackPath}?returnTo=${encodeURIComponent(returnTo)}`
  const challengeUrl = `${cfg.baseUrl}/identity/account/external/${encodeURIComponent(provider)}`
    + `?returnUrl=${encodeURIComponent(callbackUrl)}`

  return sendRedirect(event, challengeUrl, 302)
})
