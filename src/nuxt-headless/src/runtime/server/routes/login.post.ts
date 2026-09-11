import { defineEventHandler, readBody, createError } from 'h3'
import { useHeadlessConfig } from '../utils/config'
import { setUserSession } from '../utils/session'
import type { HuiaTokenResponse } from '../../types'

export default defineEventHandler(async (event) => {
  const cfg = useHeadlessConfig(event)
  const body = await readBody(event)

  const url = `${cfg.huia.baseUrl}/${cfg.huia.tenant}/identity/login`
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  const data = await res.json()

  if (!res.ok) {
    throw createError({
      statusCode: res.status,
      statusMessage: data.error_description || data.title || 'Login failed',
      data,
    })
  }

  if (data.requiresTwoFactor) {
    return { requiresTwoFactor: true }
  }

  const session = await setUserSession(event, data as HuiaTokenResponse)
  return { ok: true, session }
})
