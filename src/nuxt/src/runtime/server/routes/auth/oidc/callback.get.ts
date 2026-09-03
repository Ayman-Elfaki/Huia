import { defineEventHandler, sendRedirect } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { completeAuthorization } from '../../../utils/oidc'
import { setUserSession } from '../../../utils/session'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  try {
    const { tokens, claims, returnTo } = await completeAuthorization(event)
    await setUserSession(event, { tokens, claims })
    return sendRedirect(event, returnTo || '/', 302)
  }
  catch (err) {
    const message = err instanceof Error ? err.message : ''
    const code
      = /state/i.test(message) ? 'state_mismatch'
        : /issuer/i.test(message) ? 'issuer_mismatch'
          : /par_expired|request_uri/i.test(message) ? 'par_expired'
            : 'callback_failed'
    return sendRedirect(event, `${cfg.errorPath}?auth_error=${code}`, 302)
  }
})
