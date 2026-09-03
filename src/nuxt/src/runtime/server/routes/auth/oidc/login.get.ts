import { defineEventHandler, getQuery, sendRedirect } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { beginAuthorization } from '../../../utils/oidc'
import { sanitizeReturnTo } from '../../../utils/tokens'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const q = getQuery(event)
  const returnTo = sanitizeReturnTo(typeof q.returnTo === 'string' ? q.returnTo : undefined)

  const extra: Record<string, string> = {}
  for (const key of cfg.allowedAuthParams) {
    const v = q[key]
    if (typeof v === 'string' && v.length > 0) extra[key] = v
  }

  try {
    const { redirectTo } = await beginAuthorization(event, { returnTo, extra })
    return sendRedirect(event, redirectTo, 302)
  }
  catch (err) {
    const reason = err instanceof Error && err.message.startsWith('par_required') ? 'par_required' : 'login_failed'
    return sendRedirect(event, `${cfg.errorPath}?auth_error=${reason}`, 302)
  }
})
