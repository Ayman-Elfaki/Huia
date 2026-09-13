import { defineEventHandler, readBody, createError } from 'h3'
import { resolveAuthConfig } from '../../utils/config'
import { resendConfirmationEmailAsync, BackendError } from '../../utils/backend'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const { email } = await readBody<{ email: string }>(event)

  try {
    await resendConfirmationEmailAsync(cfg, email)
    return { ok: true }
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'resend_confirmation_failed', data: err.problem })
    }
    throw err
  }
})
