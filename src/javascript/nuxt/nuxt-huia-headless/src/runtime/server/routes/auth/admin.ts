import { defineEventHandler, getQuery, readRawBody, createError, getRequestHeader, setResponseStatus } from 'h3'
import { resolveAuthConfig } from '../../utils/config'
import { getAccessToken } from '../../utils/session'

export default defineEventHandler(async (event) => {
  const cfg = resolveAuthConfig(event)
  const token = await getAccessToken(event)
  if (!token) {
    throw createError({ statusCode: 401, statusMessage: 'Unauthorized' })
  }

  const fullPath = event.path || event.node.req.url || ''
  const pathname = fullPath.split('?')[0] || ''
  const adminIdx = pathname.indexOf('/admin')
  const subpath = adminIdx >= 0 ? pathname.substring(adminIdx + '/admin'.length).replace(/^\/+/, '') : pathname.replace(/^\/+/, '')
  const query = getQuery(event)
  const qs = new URLSearchParams(query as Record<string, string>).toString()
  const targetUrl = `${cfg.baseUrl}/admin/${subpath}${qs ? `?${qs}` : ''}`

  const method = event.method.toUpperCase()
  const hasBody = ['POST', 'PUT', 'PATCH'].includes(method)
  const rawBody = hasBody ? await readRawBody(event) : undefined

  const headers: Record<string, string> = {
    authorization: `Bearer ${token}`,
  }
  const contentType = getRequestHeader(event, 'content-type')
  if (contentType) {
    headers['content-type'] = contentType
  }

  const res = await fetch(targetUrl, {
    method,
    headers,
    body: rawBody,
  })

  setResponseStatus(event, res.status)

  if (res.status === 204) {
    return null
  }

  const text = await res.text()
  if (!res.ok) {
    let errData: unknown = text
    try {
      errData = JSON.parse(text)
    }
    catch {}
    throw createError({
      statusCode: res.status,
      statusMessage: res.statusText,
      data: errData,
    })
  }

  if (!text) return null
  try {
    return JSON.parse(text)
  }
  catch {
    return text
  }
})
