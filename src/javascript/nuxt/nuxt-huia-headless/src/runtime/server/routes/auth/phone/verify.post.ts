import { defineEventHandler, readBody, createError } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { phoneVerifyAsync, meAsync, BackendError } from '../../../utils/backend'
import { setUserSession } from '../../../utils/session'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const body = await readBody<{ flowId: string, code: string }>(event)

  try {
    const result = await phoneVerifyAsync(cfg, body)
    if (result.requiresProfile) {
      return { flowId: result.flowId, requiresProfile: true as const }
    }

    const me = await meAsync(cfg, result.accessToken)
    const session = await setUserSession(event, result, me)
    return { user: session.user }
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'phone_verify_failed', data: err.problem })
    }
    throw err
  }
})
