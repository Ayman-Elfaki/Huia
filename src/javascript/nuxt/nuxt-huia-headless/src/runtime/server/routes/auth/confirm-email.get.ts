import { defineEventHandler, getQuery, createError } from 'h3'
import { resolveAuthConfig } from '../../utils/config'
import { confirmEmailAsync, BackendError } from '../../utils/backend'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const q = getQuery(event)
  const userId = String(q.userId ?? '')
  const code = String(q.code ?? '')
  const changedEmail = typeof q.changedEmail === 'string' ? q.changedEmail : undefined

  try {
    await confirmEmailAsync(cfg, userId, code, changedEmail)
    return { ok: true }
  }
  catch (err) {
    if (err instanceof BackendError) {
      throw createError({ statusCode: err.status, statusMessage: 'confirm_email_failed', data: err.problem })
    }
    throw err
  }
})
