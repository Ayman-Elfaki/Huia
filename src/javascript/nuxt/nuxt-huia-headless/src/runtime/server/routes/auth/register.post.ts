import { defineEventHandler, readBody, createError } from 'h3'
import { resolveAuthConfig } from '../../utils/config'
import { registerAsync, BackendError } from '../../utils/backend'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const body = await readBody<{ email: string, password: string, firstName: string, lastName: string }>(event)

  try {
    await registerAsync(cfg, body)
    return { ok: true }
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'register_failed', data: err.problem })
    }
    throw err
  }
})
