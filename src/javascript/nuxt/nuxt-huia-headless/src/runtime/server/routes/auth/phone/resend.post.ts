import { defineEventHandler, readBody, createError } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { phoneResendAsync, BackendError } from '../../../utils/backend'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const body = await readBody<{ flowId: string, captchaResponse?: string }>(event)

  try {
    return await phoneResendAsync(cfg, body)
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'phone_resend_failed', data: err.problem })
    }
    throw err
  }
})
