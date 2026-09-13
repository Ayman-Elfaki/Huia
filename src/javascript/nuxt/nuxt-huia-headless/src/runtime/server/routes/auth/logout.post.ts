import { defineEventHandler } from 'h3'
import { clearUserSession } from '../../utils/session'

/**
 * Local-only logout: clears the session cookie and the stored token record. Huia.Headless's bearer
 * tokens are the framework's stock (non-revocable) `AddBearerToken` format, so there is no server
 * session to end upstream — unlike `nuxt-huia-oidc`'s RP-initiated logout, this never redirects.
 */
export default defineEventHandler(async (event) => {
  await clearUserSession(event)
  return { ok: true }
})
