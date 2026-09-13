import { defineEventHandler, readBody, createError } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { phoneCompleteProfileAsync, meAsync, BackendError } from '../../../utils/backend'
import { setUserSession } from '../../../utils/session'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const body = await readBody<{ flowId: string, firstName: string, lastName: string }>(event)

  try {
    const tokens = await phoneCompleteProfileAsync(cfg, body)
    const me = await meAsync(cfg, tokens.accessToken)
    const session = await setUserSession(event, tokens, me)
    return { user: session.user }
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'phone_complete_profile_failed', data: err.problem })
    }
    throw err
  }
})
