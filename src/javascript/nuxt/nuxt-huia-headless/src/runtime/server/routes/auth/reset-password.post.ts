import { defineEventHandler, readBody, createError } from 'h3'
import { resolveAuthConfig } from '../../utils/config'
import { resetPasswordAsync, BackendError } from '../../utils/backend'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const body = await readBody<{ email: string, resetCode: string, newPassword: string }>(event)

  try {
    await resetPasswordAsync(cfg, body)
    return { ok: true }
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'reset_password_failed', data: err.problem })
    }
    throw err
  }
})
