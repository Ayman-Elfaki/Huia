import { defineEventHandler, setResponseHeader } from 'h3'
import { getUserSession } from '../../utils/session'

/**
 * Forces a session freshness check. Normally transparent (`getUserSession`/`getAccessToken` refresh
 * early on demand), but exposed as its own route for a client that wants to eagerly refresh — e.g.
 * right before a burst of API calls, or on regaining focus after the tab was backgrounded.
 */
export default defineEventHandler(async (event) => {
  setResponseHeader(event, 'Cache-Control', 'no-store')
  return getUserSession(event)
})
