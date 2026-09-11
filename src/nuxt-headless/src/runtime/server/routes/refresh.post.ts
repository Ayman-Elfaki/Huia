import { defineEventHandler, createError } from 'h3'
import { useHeadlessConfig } from '../utils/config'
import { ensureFreshTokens } from '../utils/refresh'

export default defineEventHandler(async (event) => {
  const cfg = useHeadlessConfig(event)
  const fresh = await ensureFreshTokens(event, cfg)

  if (!fresh) {
    throw createError({
      statusCode: 401,
      statusMessage: 'Session expired',
    })
  }

  return { ok: true }
})
