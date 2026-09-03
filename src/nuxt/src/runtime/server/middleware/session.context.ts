import { defineEventHandler } from 'h3'
import { getUserSession } from '../utils/session'

/**
 * Runs on every request. Populates `event.context.huiaAuth` (memoised inside `getUserSession`) so the
 * Nuxt server plugin can seed `useState('huia-auth:session')` synchronously during SSR. Errors are
 * swallowed here so a transient storage/OP hiccup never 500s an otherwise-anonymous request.
 */
export default defineEventHandler(async (event) => {
  await getUserSession(event).catch(() => {})
})
