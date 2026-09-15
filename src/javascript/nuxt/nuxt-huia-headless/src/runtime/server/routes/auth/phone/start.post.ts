import { defineEventHandler, readBody, createError } from 'h3'
import { resolveAuthConfig } from '../../../utils/config'
import { phoneStartAsync, BackendError } from '../../../utils/backend'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const body = await readBody<{ phoneNumber: string, country?: string, captchaResponse?: string }>(event)

  try {
    return await phoneStartAsync(cfg, body)
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'phone_start_failed', data: err.problem })
    }
    throw err
  }
})
