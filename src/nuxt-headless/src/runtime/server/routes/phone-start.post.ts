import { defineEventHandler, readBody, createError } from 'h3'
import { useHeadlessConfig } from '../utils/config'

export default defineEventHandler(async (event) => {
  const cfg = useHeadlessConfig(event)
  const body = await readBody(event)

  const url = `${cfg.huia.baseUrl}/${cfg.huia.tenant}/identity/phone/login/start`
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  const data = await res.json()

  if (!res.ok) {
    throw createError({
      statusCode: res.status,
      statusMessage: data.error_description || data.title || 'Phone login request failed',
      data,
    })
  }

  return { ok: true, message: data.message }
})
