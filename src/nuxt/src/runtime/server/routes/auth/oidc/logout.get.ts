import { defineEventHandler, getQuery, getRequestURL, sendRedirect } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { buildLogoutUrl } from '../../../utils/oidc'
import { clearUserSession, getSecureTokenRecord } from '../../../utils/session'
import { sanitizeReturnTo, isJwtExpired } from '../../../utils/tokens'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const returnTo = sanitizeReturnTo(String(getQuery(event).returnTo ?? '/'))
  const postLogoutRedirectUri = new URL(returnTo, getRequestURL(event).origin).href

  const record = await getSecureTokenRecord(event)
  await clearUserSession(event)

  if (cfg.logout.rpInitiated && record?.idToken && !isJwtExpired(record.idToken)) {
    return sendRedirect(event, await buildLogoutUrl(event, {
      idTokenHint: record.idToken,
      postLogoutRedirectUri,
    }), 302)
  }
  if (cfg.logout.rpInitiated) {
    return sendRedirect(event, await buildLogoutUrl(event, { postLogoutRedirectUri }), 302)
  }
  return sendRedirect(event, postLogoutRedirectUri, 302)
})
