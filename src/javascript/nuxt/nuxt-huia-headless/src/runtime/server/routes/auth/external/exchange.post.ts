import { defineEventHandler, readBody, createError } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { externalExchangeAsync, meAsync, BackendError } from '../../../utils/backend'
import { setUserSession } from '../../../utils/session'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const body = await readBody<{ code: string }>(event)

  try {
    const result = await externalExchangeAsync(cfg, body.code)
    if (result.requiresProfile) {
      return {
        code: result.code,
        requiresProfile: true as const,
        email: result.email,
        firstName: result.firstName,
        lastName: result.lastName,
      }
    }

    const me = await meAsync(cfg, result.accessToken)
    const session = await setUserSession(event, result, me)
    return { user: session.user }
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'external_exchange_failed', data: err.problem })
    }
    throw err
  }
})
